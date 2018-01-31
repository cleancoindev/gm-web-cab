﻿using Goldmint.Common;
using Goldmint.CoreLogic.Services.Acquiring;
using Goldmint.CoreLogic.Services.Blockchain;
using Goldmint.CoreLogic.Services.Mutex;
using Goldmint.CoreLogic.Services.Mutex.Impl;
using Goldmint.CoreLogic.Services.Ticket;
using Goldmint.DAL;
using Goldmint.DAL.Models;
using Goldmint.DAL.Models.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Goldmint.CoreLogic.Finance.Fiat {

	public static class DepositQueue {

		// TODO: detach processing items from db context

		/// <summary>
		/// Attempt to start deposit
		/// </summary>
		public static async Task<DepositResult> StartDepositWithCard(IServiceProvider services, CardPayment payment) {

			if (payment.Type != CardPaymentType.Deposit) throw new ArgumentException("Incorrect payment type");
			if (payment.AmountCents <= 0) throw new ArgumentException("Amount must be greater than zero");
			if (payment.Card == null) throw new ArgumentException("Card not included");
			if (payment.User == null) throw new ArgumentException("User not included");

			var logger = services.GetLoggerFor(typeof(DepositQueue));
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var cardAcquirer = services.GetRequiredService<ICardAcquirer>();
			var ticketDesk = services.GetRequiredService<ITicketDesk>();

			return await StartDeposit(services, payment.User, payment.Currency, payment.AmountCents, async () => {

				try {
					await ticketDesk.UpdateCardDepositTicket(payment.DeskTicketId, TicketStatus.Opened, "Payment charge started");
				}
				catch { }

				// charge payment
				var chargeResult = await cardAcquirer.DoPaymentCharge(payment.GWTransactionId);
				try {
					payment.Status = chargeResult.Success ? CardPaymentStatus.Success : CardPaymentStatus.Failed;
					payment.ProviderMessage = chargeResult.ProviderMessage;
					payment.ProviderStatus = chargeResult.ProviderStatus;
					payment.TimeCompleted = DateTime.UtcNow;

					dbContext.Entry(payment).State = EntityState.Modified;
					await dbContext.SaveChangesAsync();
					dbContext.Entry(payment).State = EntityState.Detached;

				}
				catch (Exception e) {
					logger?.Error(e, $"Failed to update payment status while charging deposit payment #{payment.Id}");
				}

				try {
					if (chargeResult.Success) {
						await ticketDesk.UpdateCardDepositTicket(payment.DeskTicketId, TicketStatus.Opened, "Payment charged");
					}
					else {
						await ticketDesk.UpdateCardDepositTicket(payment.DeskTicketId, TicketStatus.Cancelled, "Payment charge failed");
					}
				}
				catch { }

				return new Deposit() {
					User = payment.User,
					Status = DepositStatus.Initial,
					Currency = payment.Currency,
					AmountCents = payment.AmountCents,
					Source = DepositSource.CreditCard,
					SourceId = payment.Id,
					DeskTicketId = payment.DeskTicketId,
					TimeCreated = DateTime.UtcNow,
					TimeNextCheck = DateTime.UtcNow,
				};
			});
		}

		/// <summary>
		/// Attempt to enqueue deposit
		/// </summary>
		public static async Task<DepositResult> StartDeposit(IServiceProvider services, User user, FiatCurrency currency, long amountCents, Func<Task<Deposit>> onSuccess) {

			if (amountCents <= 0) throw new ArgumentException("Amount must be greater than zero");

			var logger = services.GetLoggerFor(typeof(DepositQueue));
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var ticketDesk = services.GetRequiredService<ITicketDesk>();

			// lock deposit attempt for user
			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.DepositEnqueue, user.Id)
			;

			return await mutexBuilder.LockAsync(async (ok) => {
				if (ok) {

					// get limit
					var limit = await UserAccount.GetCurrentFiatDepositLimit(services, currency, user);
					if (amountCents <= limit) {
						try {
							var deposit = await onSuccess();
							if (deposit == null) throw new Exception("Got null deposit object");

							try {
								dbContext.Deposit.Add(deposit);
								await dbContext.SaveChangesAsync();
							}
							catch (Exception e) {
								await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Cancelled, "DB failed while deposit enqueue");
								throw e;
							}

							try {
								await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Opened, "Deposit successfully enqueued");
							}
							catch { }

							return new DepositResult() {
								Status = FiatEnqueueStatus.Success,
								DepositId = deposit.Id,
							};
						}
						catch (Exception e) {
							return new DepositResult() {
								Status = FiatEnqueueStatus.Error,
								Error = e,
							};
						}
					}
					return new DepositResult() {
						Status = FiatEnqueueStatus.Limit,
					};
				}
				else {
					return new DepositResult() {
						Status = FiatEnqueueStatus.Error,
						Error = new Exception("Faield to lock deposit queue"),
					};
				}
			});
		}

		/// <summary>
		/// Processes deposit record depending on it's current status
		/// </summary>
		public static async Task ProcessDeposit(IServiceProvider services, Deposit deposit) {

			if (deposit.User == null) throw new ArgumentException("User not included");

			var logger = services.GetLoggerFor(typeof(DepositQueue));
			var appConfig = services.GetRequiredService<AppConfig>();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var ethereumReader = services.GetRequiredService<IEthereumReader>();
			var ethereumWriter = services.GetRequiredService<IEthereumWriter>();
			var ticketDesk = services.GetRequiredService<ITicketDesk>();

			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.DepositCheck, deposit.Id)
			;

			await mutexBuilder.LockAsync(async (ok) => {
				if (ok) {

					// oups, deposit is finalized already
					if (deposit.Status == DepositStatus.Success || deposit.Status == DepositStatus.Failed) {
						return;
					}

					try {
						// set next check time
						deposit.TimeNextCheck = DateTime.UtcNow + QueuesUtils.GetNextCheckDelay(deposit.TimeCreated, TimeSpan.FromSeconds(15), 3);

						// initiate blockchain transaction
						if (deposit.Status == DepositStatus.Initial) {

							// update status to prevent double spending
							deposit.Status = DepositStatus.BlockchainInit;
							dbContext.Entry(deposit).State = EntityState.Modified;
							await dbContext.SaveChangesAsync();
							dbContext.Entry(deposit).State = EntityState.Detached;

							try {
								await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Opened, "Blockchain transaction initiated");
							}
							catch { }

							// launch transaction
							if (string.IsNullOrWhiteSpace(deposit.EthTransactionId)) {
								var txid = await ethereumWriter.ChangeUserFiatBalance(deposit.User.Id, deposit.Currency, deposit.AmountCents);
								deposit.EthTransactionId = txid;
							}

							// set new status
							deposit.Status = DepositStatus.BlockchainConfirm;
							dbContext.Entry(deposit).State = EntityState.Modified;
							await dbContext.SaveChangesAsync();
							dbContext.Entry(deposit).State = EntityState.Detached;

							// update ticket safely
							try {
								await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Opened, "Blockchain transaction checking started");
							}
							catch { }
						}
						
						// initiating blockchain transaction
						else if (deposit.Status == DepositStatus.BlockchainInit) {
							// actually should not get into this section.
							// see initial status action above
						}
						
						// check confirmation
						else if (deposit.Status == DepositStatus.BlockchainConfirm) {
							var result = await ethereumReader.CheckTransaction(deposit.EthTransactionId);

							// final
							if (result == BlockchainTransactionStatus.Success || result == BlockchainTransactionStatus.Failed) {
								deposit.Status = result == BlockchainTransactionStatus.Success ? DepositStatus.Success : DepositStatus.Failed;
								deposit.TimeCompleted = DateTime.UtcNow;
							}

							dbContext.Entry(deposit).State = EntityState.Modified;
							await dbContext.SaveChangesAsync();
							dbContext.Entry(deposit).State = EntityState.Detached;

							try {
								if (deposit.Status == DepositStatus.Success) {
									await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Success, "Deposit has been saved on blockchain");
								}
								if (deposit.Status == DepositStatus.Failed) {
									await ticketDesk.UpdateCardDepositTicket(deposit.DeskTicketId, TicketStatus.Cancelled, "Deposit has not been saved on blockchain");
								}
							}
							catch { }
						}
					}
					catch (Exception e) {
						logger.Error(e, "Failed to update deposit #{0}", deposit.Id);
					}
				}
			});
		}

		// ---

		/// <summary>
		/// Deposit attempt result
		/// </summary>
		public sealed class DepositResult {

			public FiatEnqueueStatus Status { get; internal set; }
			public long? DepositId { get; internal set; }
			public Exception Error { get; internal set; }
		}
	}
}

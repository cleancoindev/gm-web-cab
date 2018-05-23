﻿using Goldmint.Common;
using Goldmint.DAL.Models;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Goldmint.CoreLogic.Services.The1StPayments;
using Microsoft.Extensions.DependencyInjection;
using Goldmint.CoreLogic.Services.Mutex;
using Goldmint.DAL;
using Goldmint.CoreLogic.Services.Oplog;
using Microsoft.AspNetCore.Hosting;
using Goldmint.CoreLogic.Services.Mutex.Impl;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace Goldmint.CoreLogic.Finance {

	public static class The1StPaymentsProcessing {

		/// <summary>
		/// New card input data operation to enqueue
		/// </summary>
		public static CreditCardPayment CreateCardDataInputPayment(UserCreditCard card, CardPaymentType type, string transactionId, string gwTransactionId, string oplogId) {

			// if (card.User == null) throw new ArgumentException("User not included");

			// new deposit payment
			return new CreditCardPayment() {
				CardId = card.Id,
				TransactionId = transactionId,
				GwTransactionId = gwTransactionId,
				Type = type,
				UserId = card.UserId,
				Currency = FiatCurrency.Usd,
				AmountCents = 0,
				Status = CardPaymentStatus.Pending,
				OplogId = oplogId,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow.AddSeconds(15 * 60),
			};
		}

		/// <summary>
		/// New verification payment
		/// </summary>
		public static async Task<CreditCardPayment> CreateVerificationPayment(IServiceProvider services, UserCreditCard card, string oplogId) {

			// if (card.User == null) throw new ArgumentException("User not included");

			var cardAcquirer = services.GetRequiredService<The1StPayments>();

			var amountCents = card.VerificationAmountCents;
			var tid = GenerateTransactionId();

			var gwTransactionId = await cardAcquirer.StartPaymentCharge(new StartPaymentCharge() {
				AmountCents = (int)amountCents,
				TransactionId = tid,
				InitialGWTransactionId = card.GwInitialDepositCardTransactionId,
				Purpose = "Card verification at goldmint.io"
			});

			return new CreditCardPayment() {
				CardId = card.Id,
				TransactionId = tid,
				GwTransactionId = gwTransactionId,
				Type = CardPaymentType.Verification,
				UserId = card.UserId,
				Currency = FiatCurrency.Usd,
				AmountCents = amountCents,
				Status = CardPaymentStatus.Pending,
				OplogId = oplogId,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow.AddSeconds(0),
			};
		}

		/// <summary>
		/// New deposit payment
		/// </summary>
		public static async Task<CreditCardPayment> CreateDepositPayment(IServiceProvider services, UserCreditCard card, FiatCurrency currency, long amountCents, long buyRequestId, string oplogId) {

			if (amountCents <= 0) throw new ArgumentException("Amount must be greater than zero");
			if (card.State != CardState.Verified) throw new ArgumentException("Card not verified");
			// if (card.User == null) throw new ArgumentException("User not included");

			var cardAcquirer = services.GetRequiredService<The1StPayments>();

			// ---

			var tid = GenerateTransactionId();

			var gwTransactionId = await cardAcquirer.StartPaymentCharge(new StartPaymentCharge() {
				AmountCents = (int)amountCents,
				TransactionId = tid,
				InitialGWTransactionId = card.GwInitialDepositCardTransactionId,
				Purpose = "Deposit at goldmint.io",
				DynamicDescriptor = null,
			});

			return new CreditCardPayment() {
				CardId = card.Id,
				TransactionId = tid,
				GwTransactionId = gwTransactionId,
				Type = CardPaymentType.Deposit,
				UserId = card.UserId,
				Currency = currency,
				AmountCents = amountCents,
				Status = CardPaymentStatus.Pending,
				RelatedExchangeRequestId = buyRequestId,
				OplogId = oplogId,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow.AddSeconds(0),
			};
		}

		/// <summary>
		/// New payment refund
		/// </summary>
		public static CreditCardPayment CreateRefundPayment(CreditCardPayment refPayment, string oplogId) {

			if (refPayment.Type != CardPaymentType.Deposit && refPayment.Type != CardPaymentType.Verification) {
				throw new ArgumentException("Ref payment must be of deposit or verification type");
			}
			if (refPayment.Status != CardPaymentStatus.Success) throw new ArgumentException("Cant refund unsuccessful payment");
			if (refPayment.CreditCard == null) throw new ArgumentException("Card not included");
			// if (refPayment.User == null) throw new ArgumentException("User not included");

			// new refund payment
			return new CreditCardPayment() {
				CreditCard = refPayment.CreditCard,
				TransactionId = GenerateTransactionId(),
				GwTransactionId = "", // empty until charge
				RefPayment = refPayment,
				Type = CardPaymentType.Refund,
				UserId = refPayment.UserId,
				Currency = refPayment.Currency,
				AmountCents = refPayment.AmountCents,
				Status = CardPaymentStatus.Pending,
				OplogId = oplogId,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow.AddSeconds(15 * 60),
			};
		}

		/// <summary>
		/// New withdraw payment
		/// </summary>
		public static async Task<CreditCardPayment> CreateWithdrawPayment(IServiceProvider services, UserCreditCard card, FiatCurrency currency, long amountCents, long sellRequestId, string oplogId) {

			if (amountCents <= 0) throw new ArgumentException("Amount must be greater than zero");
			if (card.State != CardState.Verified) throw new ArgumentException("Card not verified");
			// if (card.User == null) throw new ArgumentException("User not included");

			var cardAcquirer = services.GetRequiredService<The1StPayments>();

			// ---

			var tid = GenerateTransactionId();

			var gwTransactionId = await cardAcquirer.StartCreditCharge(new StartCreditCharge() {
				AmountCents = (int)amountCents,
				TransactionId = tid,
				InitialGWTransactionId = card.GwInitialWithdrawCardTransactionId,
				Purpose = "Withdraw at goldmint.io",
				DynamicDescriptor = null,
			});

			return new CreditCardPayment() {
				CardId = card.Id,
				TransactionId = tid,
				GwTransactionId = gwTransactionId,
				Type = CardPaymentType.Withdraw,
				UserId = card.UserId,
				Currency = currency,
				AmountCents = amountCents,
				Status = CardPaymentStatus.Pending,
				RelatedExchangeRequestId = sellRequestId,
				OplogId = oplogId,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow.AddSeconds(0),
			};
		}

		// ---

		/// <summary>
		/// Checks card data input transaction (actually this is not payment)
		/// </summary>
		public static async Task<ProcessPendingCardDataInputPaymentResult> ProcessPendingCardDataInputPayment(IServiceProvider services, long paymentId) {

			var logger = services.GetLoggerFor(typeof(The1StPaymentsProcessing));
			var appConfig = services.GetRequiredService<AppConfig>();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var cardAcquirer = services.GetRequiredService<The1StPayments>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();
			var hostingEnv = services.GetRequiredService<IHostingEnvironment>();

			// lock payment updating by payment id
			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.CardPaymentCheck, paymentId)
			;

			// pending by default
			var ret = new ProcessPendingCardDataInputPaymentResult() {
				Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.Pending,
				VerificationPaymentId = null,
			};

			return await mutexBuilder.CriticalSection<ProcessPendingCardDataInputPaymentResult>(async (ok) => {
				if (ok) {

					// get payment from db
					var payment = await (
						from p in dbContext.CreditCardPayment
						where
						p.Id == paymentId &&
						(p.Type == CardPaymentType.CardDataInputSMS || p.Type == CardPaymentType.CardDataInputCRD || p.Type == CardPaymentType.CardDataInputP2P) &&
						p.Status == CardPaymentStatus.Pending
						select p
					)
						.Include(p => p.CreditCard)
						.Include(p => p.User).ThenInclude(u => u.UserVerification)
						.AsNoTracking()
						.FirstOrDefaultAsync()
					;

					// not found
					if (payment == null) {
						ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.NothingToDo;
						return ret;
					}

					// update payment
					bool finalized = false;
					string cardHolder = null;
					string cardMask = null;
					{

						// query acquirer
						var result = await cardAcquirer.CheckCardStored(payment.GwTransactionId);

						// set new status
						switch (result.Status) {

							case CardGatewayTransactionStatus.Success:
								payment.Status = CardPaymentStatus.Success; // skip confirmed state
								finalized = true;
								break;

							case CardGatewayTransactionStatus.Failed:
							case CardGatewayTransactionStatus.NotFound:
								payment.Status = CardPaymentStatus.Failed;
								finalized = true;
								break;

							default:
								payment.Status = CardPaymentStatus.Pending;
								break;
						}

						// set additinal fields
						payment.ProviderStatus = result.ProviderStatus;
						payment.ProviderMessage = result.ProviderMessage;
						payment.TimeNextCheck = DateTime.UtcNow + QueuesUtils.GetNextCheckDelay(payment.TimeCreated, TimeSpan.FromSeconds(15 * 60), 1);

						// get card data if possible
						if (result.CardHolder != null) {
							cardHolder = result.CardHolder;
							cardMask = result.CardMask;
						}

						// finalize
						if (finalized) {
							payment.TimeCompleted = DateTime.UtcNow;
						}
					}

					// update payment
					dbContext.Update(payment);
					await dbContext.SaveChangesAsync();

					// now is final
					if (finalized) {

						CreditCardPayment verificationPaymentEnqueued = null;
						var card = payment.CreditCard;

						// delete card on any data mismatch
						var cardPrevState = card.State;
						card.State = CardState.Deleted;

						if (payment.Status == CardPaymentStatus.Success) {

							// set next step
							if (cardHolder != null &&
								cardMask != null &&
								User.HasFilledPersonalData(payment.User?.UserVerification) &&
								cardHolder.Contains(payment.User?.UserVerification.FirstName) &&
								cardHolder.Contains(payment.User?.UserVerification.LastName)
							) {

								// check for duplicate
								if (
									await dbContext.UserCreditCard.CountAsync(_ =>
										_.UserId == payment.UserId &&
										_.State != CardState.Deleted &&
										_.Id != card.Id &&
										_.CardMask == cardMask
									) > 0
								) {

									card.State = CardState.Deleted;
									card.HolderName = cardHolder;
									card.CardMask = cardMask;

									try {
										await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Failed, $"Card with the same mask exists {cardMask}");
									}
									catch {
									}

									ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.DuplicateCard;
								}

								// this is 1st step - deposit data
								else if (cardPrevState == CardState.InputDepositData) {
									card.State = CardState.InputWithdrawData;
									card.HolderName = cardHolder;
									card.CardMask = cardMask;

									try {
										await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, "Provided card data on first step is saved");
									}
									catch {
									}

									// ok
									ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.DepositDataOk;
								}

								// this is 2nd step - withdraw data - must be the same card
								else if (cardPrevState == CardState.InputWithdrawData && card.CardMask != null) {

									// mask matched
									if (card.CardMask == cardMask || !(hostingEnv?.IsProduction() ?? true)) {

										card.State = CardState.Payment;

										try {
											await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, "Provided card data on second step is saved");
										}
										catch { }

										// enqueue verification payment
										try {
											var verPayment = await CreateVerificationPayment(
												services: services,
												card: card,
												oplogId: payment.OplogId
											);
											dbContext.CreditCardPayment.Add(verPayment);
											verificationPaymentEnqueued = verPayment;

											// ok, for now
											ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.WithdrawDataOk;
										}
										catch (Exception e) {
											logger?.Error(e, $"Failed to start verification charge for this payment");

											// failed to charge
											ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.FailedToChargeVerification;
										}
									}
									else {
										// mask mismatched
										ret.Result = ProcessPendingCardDataInputPaymentResult.ResultEnum.WithdrawCardDataMismatched;

										await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Failed, "Provided card data is mismatched");
									}
								}
							}
						}
						else if (payment.Status == CardPaymentStatus.Failed) {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Failed, $"Card data input step is unsuccessful on a gateway side");
						}

						// update card state
						dbContext.Update(card);
						await dbContext.SaveChangesAsync();

						if (verificationPaymentEnqueued != null) {
							ret.VerificationPaymentId = verificationPaymentEnqueued.Id;

							try {
								await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, $"Verification payment #{verificationPaymentEnqueued.Id} enqueued");
							}
							catch { }
						}
					}
				}

				return ret;
			});
		}

		/// <summary>
		/// Checks verification card payment vs acquirer with exclusive write access on this payment
		/// </summary>
		/// <exception cref="Exception"></exception>
		public static async Task<ProcessVerificationPaymentResult> ProcessVerificationPayment(IServiceProvider services, long paymentId) {

			var logger = services.GetLoggerFor(typeof(The1StPaymentsProcessing));
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var cardAcquirer = services.GetRequiredService<The1StPayments>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();

			// lock payment updating by payment id
			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.CardPaymentCheck, paymentId)
			;

			// by default
			var ret = new ProcessVerificationPaymentResult() {
				Result = ProcessVerificationPaymentResult.ResultEnum.Pending,
				RefundPaymentId = null,
			};

			return await mutexBuilder.CriticalSection<ProcessVerificationPaymentResult>(async (ok) => {
				if (ok) {

					// get payment from db
					var payment = await (
						from p in dbContext.CreditCardPayment
						where p.Id == paymentId && p.Type == CardPaymentType.Verification && p.Status == CardPaymentStatus.Pending
						select p
					)
						.Include(p => p.CreditCard)
						.Include(p => p.User)
						.ThenInclude(u => u.UserVerification)
						.AsNoTracking()
						.FirstOrDefaultAsync()
					;

					// not found
					if (payment == null) {
						ret.Result = ProcessVerificationPaymentResult.ResultEnum.NothingToDo;
						return ret;
					}

					try {
						await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, $"Charging {payment.AmountCents} cents");
					}
					catch { }

					// prevent double spending
					payment.Status = CardPaymentStatus.Charging;
					dbContext.Update(payment);
					await dbContext.SaveChangesAsync();

					// charge
					ChargeResult result = null;
					try {
						result = await cardAcquirer.DoPaymentCharge(payment.GwTransactionId);
					}
					catch (Exception e) {
						logger?.Error(e, $"Failed to charge of payment #{payment.Id}");
					}

					// update ticket
					try {
						if (result?.Success ?? false) {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, "Charged successfully");
						}
						else {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Failed, "Charge failed");
						}
					}
					catch { }

					// assume failed by default
					payment.Status = CardPaymentStatus.Failed;
					payment.ProviderStatus = result?.ProviderStatus;
					payment.ProviderMessage = result?.ProviderMessage;
					payment.TimeCompleted = DateTime.UtcNow;
					// payment.TimeNextCheck = doesn't matter

					// payment will be updated
					dbContext.Update(payment);

					CreditCardPayment refundEnqueued = null;

					// success
					if (result?.Success ?? false) {

						// new status
						payment.Status = CardPaymentStatus.Success;

						// new step on card verification
						payment.CreditCard.State = CardState.Verification;
						dbContext.Update(payment.CreditCard);

						// refund
						try {
							var refund = CreateRefundPayment(payment, payment.OplogId);
							dbContext.CreditCardPayment.Add(refund);
							refundEnqueued = refund;

							// charged and refunded
							ret.Result = ProcessVerificationPaymentResult.ResultEnum.Refunded;
							ret.RefundPaymentId = refund.Id;
						}
						catch (Exception e) {
							logger?.Error(e, $"Failed to enqueue verification refund for payment #{payment.Id}`");

							// refund failed
							ret.Result = ProcessVerificationPaymentResult.ResultEnum.RefundFailed;
						}
					}
					// failed
					else {
						payment.CreditCard.State = CardState.Deleted;
						dbContext.Update(payment.CreditCard);

						// didnt charge
						ret.Result = ProcessVerificationPaymentResult.ResultEnum.ChargeFailed;
					}

					try {
						await dbContext.SaveChangesAsync();
					}
					catch (Exception e) {
						logger?.Error(e);
						throw e;
					}

					// update ticket
					try {
						if (refundEnqueued != null) {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, $"Refund #{refundEnqueued.Id} enqueued");
						}
					}
					catch { }
				}
				return ret;
			});
		}

		/// <summary>
		/// Process pending refunds
		/// </summary>
		/// <exception cref="Exception"></exception>
		public static async Task<bool> ProcessRefundPayment(IServiceProvider services, long paymentId) {

			var logger = services.GetLoggerFor(typeof(The1StPaymentsProcessing));
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var cardAcquirer = services.GetRequiredService<The1StPayments>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();

			// lock payment updating by payment id
			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.CardPaymentCheck, paymentId)
			;

			return await mutexBuilder.CriticalSection(async (ok) => {
				if (ok) {
					// get payment from db
					var payment = await (
						from p in dbContext.CreditCardPayment
						where p.Id == paymentId && p.Type == CardPaymentType.Refund && p.Status == CardPaymentStatus.Pending
						select p
					)
					.Include(p => p.User)
					.AsNoTracking()
					.FirstOrDefaultAsync();

					if (payment == null || payment.RelPaymentId == null) return false;

					// get ref payment
					var refPayment = await (
						from p in dbContext.CreditCardPayment
						where
						p.Id == payment.RelPaymentId.Value &&
						(p.Type == CardPaymentType.Deposit || p.Type == CardPaymentType.Verification) &&
						p.Status == CardPaymentStatus.Success
						select p
					)
					.AsNoTracking()
					.FirstOrDefaultAsync();

					if (refPayment == null) return false;

					await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, $"Refunding {payment.AmountCents} cents");

					// prevent double spending
					payment.Status = CardPaymentStatus.Charging;
					dbContext.Update(payment);
					await dbContext.SaveChangesAsync();

					// charge
					string resultGWTID = null;
					try {
						resultGWTID = await cardAcquirer.RefundPayment(new RefundPayment() {
							AmountCents = (int)payment.AmountCents,
							TransactionId = payment.TransactionId,
							RefGWTransactionId = refPayment.GwTransactionId,
						});
					}
					catch (Exception e) {
						logger?.Error(e, $"Failed to make charge of payment #{payment.Id} (refund of payment #{refPayment.Id})");
					}

					// update ticket
					try {
						if (resultGWTID != null) {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Pending, "Refunded successfully");
						}
						else {
							await ticketDesk.Update(payment.OplogId, UserOpLogStatus.Failed, $"Card verification refund #{payment.Id} failed and requires manual processing");
						}
					}
					catch { }

					payment.Status = CardPaymentStatus.Failed;
					payment.ProviderStatus = "Refund Failed";
					payment.ProviderMessage = "Refund Failed";
					payment.TimeCompleted = DateTime.UtcNow;
					// payment.TimeNextCheck = doesn't matter

					// update payment
					if (resultGWTID != null) {
						payment.Status = CardPaymentStatus.Success;
						payment.GwTransactionId = resultGWTID;
						payment.ProviderStatus = "Refund Success";
						payment.ProviderMessage = "Refund Success";
					}

					dbContext.Update(payment);
					await dbContext.SaveChangesAsync();

					return payment.Status == CardPaymentStatus.Success;
				}
				return false;
			});
		}

		// ---

		/// <summary>
		/// New card payment tx ID
		/// </summary>
		public static string GenerateTransactionId() {
			return Guid.NewGuid().ToString("N");
		}

		/// <summary>
		/// Data input payment result
		/// </summary>
		public class ProcessPendingCardDataInputPaymentResult {

			public ResultEnum Result { get; set; }
			public long? VerificationPaymentId { get; set; }

			public enum ResultEnum {
				NothingToDo,
				Pending,
				DuplicateCard,
				DepositDataOk,
				WithdrawCardDataMismatched,
				FailedToChargeVerification,
				WithdrawDataOk
			}
		}

		/// <summary>
		/// Verification payment result
		/// </summary>
		public class ProcessVerificationPaymentResult {

			public ResultEnum Result { get; set; }
			public long? RefundPaymentId { get; set; }

			public enum ResultEnum {
				NothingToDo,
				Pending,
				ChargeFailed,
				RefundFailed,
				Refunded
			}
		}
	}
}
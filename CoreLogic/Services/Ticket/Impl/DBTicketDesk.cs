﻿using Goldmint.Common;
using Goldmint.DAL;
using Goldmint.DAL.Models;
using Goldmint.DAL.Models.Identity;
using NLog;
using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Numerics;

namespace Goldmint.CoreLogic.Services.Ticket.Impl {

	public class DBTicketDesk : ITicketDesk {

		private ApplicationDbContext _dbContext;
		private ILogger _logger;

		public DBTicketDesk(ApplicationDbContext dbContext, LogFactory logFactory) {
			_dbContext = dbContext;
			_logger = logFactory.GetLoggerFor(this);
		}

		// ---

		private async Task<string> CreateTicket(long userId, string message, long? refId = null, UserOpLogStatus status = UserOpLogStatus.Pending) {
			var op = new UserOpLog() {
				Status = status,
				UserId = userId,
				RefId = refId,
				Message = message,
				TimeCreated = DateTime.UtcNow,
			};
			_dbContext.UserOpLog.Add(op);
			await _dbContext.SaveChangesAsync();
			_dbContext.Entry(op).State = EntityState.Detached;

			return op.Id.ToString();
		}
		
		// ---

		public async Task UpdateTicket(string ticketId, UserOpLogStatus status, string message) {
			if (ticketId != null && long.TryParse(ticketId, out long id)) {

				var op = await (
					from s in _dbContext.UserOpLog
					where s.Id == id
					select s
				)
					.AsNoTracking()
					.FirstAsync()
				;
				if (op != null) {
					_dbContext.Entry(op).State = EntityState.Detached;
					await CreateTicket(op.UserId, message, id, status);
				}
			}
		}

		public Task NewManualSupportTicket(string message) {
			// TODO: support tickets queue
			return Task.CompletedTask;
		}

		public async Task<string> NewCardVerification(User user, Card card) {
			return await CreateTicket(user.Id, $"New card #{card.Id} verification started with {TextFormatter.FormatAmount(card.VerificationAmountCents, FiatCurrency.USD)}");
		}

		public async Task<string> NewCardDeposit(User user, Card card, FiatCurrency currency, long amount) {
			return await CreateTicket(user.Id, $"New card deposit #? (card #{card.Id}, {card.CardMask}) started with {TextFormatter.FormatAmount(amount, currency)}");
		}

		public async Task<string> NewSwiftDeposit(User user, FiatCurrency currency, long amount) {
			return await CreateTicket(user.Id, $"New SWIFT deposit #? requested with {TextFormatter.FormatAmount(amount, currency)}");
		}

		public async Task<string> NewCardWithdraw(User user, Card card, FiatCurrency currency, long amount) {
			return await CreateTicket(user.Id, $"New card witdraw #? (card #{card.Id}, {card.CardMask}) started with {TextFormatter.FormatAmount(amount, currency)}");
		}

		public async Task<string> NewSwiftWithdraw(User user, FiatCurrency currency, long amount) {
			return await CreateTicket(user.Id, $"New SWIFT withdraw #? requested with {TextFormatter.FormatAmount(amount, currency)}");
		}

		public async Task<string> NewGoldBuying(User user, string ethAddressOrNull, FiatCurrency currency, long fiatAmount, long rate, BigInteger mntpBalance, BigInteger estimatedGoldAmount, long feeCents) {
			return await CreateTicket(user.Id, $"New gold buying #? for {TextFormatter.FormatAmount(fiatAmount, currency)} requested from {( ethAddressOrNull != null? TextFormatter.MaskEthereumAddress(ethAddressOrNull): "HW" )} at rate {TextFormatter.FormatAmount(rate, currency)}, {CoreLogic.Finance.Tokens.MntpToken.FromWei(mntpBalance)} mints, est. {CoreLogic.Finance.Tokens.GoldToken.FromWei(estimatedGoldAmount)} oz, fee {TextFormatter.FormatAmount(feeCents, currency)}");
		}

		public async Task<string> NewGoldSelling(User user, string ethAddressOrNull, FiatCurrency currency, BigInteger goldAmount, long rate, BigInteger mntpBalance, long estimatedFiatAmount, long feeCents) {
			return await CreateTicket(user.Id, $"New gold selling #? of {CoreLogic.Finance.Tokens.GoldToken.FromWei(goldAmount)} oz requested from {( ethAddressOrNull != null? TextFormatter.MaskEthereumAddress(ethAddressOrNull): "HW")} at rate {TextFormatter.FormatAmount(rate, currency)}, {CoreLogic.Finance.Tokens.MntpToken.FromWei(mntpBalance)} mints, est. {TextFormatter.FormatAmount(estimatedFiatAmount, currency)}, fee {TextFormatter.FormatAmount(feeCents, currency)}");
		}

		public async Task<string> NewGoldTransfer(User user, string ethAddress, BigInteger goldAmount) {
			return await CreateTicket(user.Id, $"New gold transfer #? of {CoreLogic.Finance.Tokens.GoldToken.FromWei(goldAmount)} oz requested from HW to {TextFormatter.MaskEthereumAddress(ethAddress)}");
		}
	}
}

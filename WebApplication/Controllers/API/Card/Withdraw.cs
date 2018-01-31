﻿using Goldmint.Common;
using Goldmint.CoreLogic.Finance.Fiat;
using Goldmint.WebApplication.Core.Policies;
using Goldmint.WebApplication.Core.Response;
using Goldmint.WebApplication.Models.API;
using Goldmint.WebApplication.Models.API.CardModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Goldmint.WebApplication.Controllers.API {

	public partial class CardController : BaseController {

		/// <summary>
		/// Deposit with card
		/// </summary>
		[AreaAuthorized]
		[HttpPost, Route("withdraw")]
		[ProducesResponseType(typeof(WithdrawView), 200)]
		public async Task<APIResponse> Withdraw([FromBody] WithdrawModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			// round cents
			var amountCents = (long)Math.Floor(model.Amount * 100d);
			model.Amount = amountCents / 100d;

			var user = await GetUserFromDb();
			var agent = GetUserAgentInfo();

			if (!CoreLogic.UserAccount.IsUserVerifiedL0(user)) {
				return APIResponse.BadRequest(APIErrorCode.AccountNotVerified);
			}

			// get card
			var card = user.Card.SingleOrDefault(
				c => c.Id == model.CardId && c.State == CardState.Verified
			);
			if (card == null) {
				return APIResponse.BadRequest(nameof(model.CardId), "Card not found");
			}

			var transId = CardPaymentQueue.GenerateTransactionId();
			var transCurrency = FiatCurrency.USD;

			// new ticket
			var ticket = await TicketDesk.CreateCardWithdrawTicket(TicketStatus.Opened, card.User.UserName, amountCents, transCurrency, "New withdraw request");

			// make payment
			var payment = await CardPaymentQueue.CreateWithdrawPayment(
				services: HttpContext.RequestServices,
				card: card,
				currency: transCurrency,
				amountCents: amountCents,
				deskTicketId: ticket
			);
			DbContext.CardPayment.Add(payment);
			DbContext.SaveChanges();

			// try
			var queryResult = await WithdrawQueue.StartWithdrawWithCard(
				services: HttpContext.RequestServices,
				payment: payment
			);

			switch (queryResult.Status) {

				case FiatEnqueueStatus.Success:

					// activity
					await CoreLogic.UserAccount.SaveActivity(
						services: HttpContext.RequestServices,
						user: user,
						type: Common.UserActivityType.CreditCard,
						comment: $"Withdrawal payment #{payment.Id} ({TextFormatter.FormatAmount(payment.AmountCents, transCurrency)}, card {card.CardMask}) initiated",
						ip: agent.Ip,
						agent: agent.Agent
					);

					return APIResponse.Success(
						new WithdrawView() {
						}
					);

				case FiatEnqueueStatus.Limit:
					return APIResponse.BadRequest(APIErrorCode.AccountWithdrawLimit);

				default:
					throw new Exception(queryResult.Error.Message);
			}
		}
	}
}

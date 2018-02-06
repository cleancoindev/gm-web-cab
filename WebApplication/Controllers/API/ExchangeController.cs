﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Goldmint.WebApplication.Core.Policies;
using Goldmint.WebApplication.Models.API.ExchangeModels;
using Goldmint.WebApplication.Core.Response;
using Goldmint.WebApplication.Models.API;
using Goldmint.Common;
using Goldmint.DAL.Models;
using System.Numerics;

namespace Goldmint.WebApplication.Controllers.API {

	[Route("api/v1/gold")]
	public class ExchangeController : BaseController {

		/// <summary>
		/// Buying request
		/// </summary>
		[AreaAuthorized]
		[HttpPost, Route("buyRequest")]
		[ProducesResponseType(typeof(BuyRequestView), 200)]
		public async Task<APIResponse> BuyRequest([FromBody] BuyRequestModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			// round cents
			var amountCents = (long)Math.Floor(model.Amount * 100d);
			model.Amount = amountCents / 100d;

			var user = await GetUserFromDb();
			var agent = GetUserAgentInfo();

			// ---

			var currency = FiatCurrency.USD;
			var mntpBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserMntpBalance(model.EthAddress);
			var fiatBalance = await EthereumObserver.GetUserFiatBalance(user.UserName, currency);
			var goldRate = await GoldRateProvider.GetGoldRate(currency);

			// estimate
			var estimated = await CoreLogic.Finance.Tokens.GoldToken.EstimateBuying(
				fiatAmountCents: amountCents,
				fiatTotalVolumeCents: fiatBalance,
				pricePerGoldOunceCents: goldRate,
				mntpBalance: mntpBalance
			);

			// ---

			// invalid amount passed
			if (amountCents != estimated.InputUsed) {
				return APIResponse.BadRequest(nameof(model.Amount), "Amount is invalid");
			}

			var ticket = await TicketDesk.CreateGoldSellingTicket(TicketStatus.Opened, user.UserName, "New gold buying request generated");

			// history
			var finHistory = new DAL.Models.FinancialHistory() {
				Type = FinancialHistoryType.GoldBuying,
				AmountCents = amountCents,
				FeeCents = estimated.ResultFeeCents,
				Currency = currency,
				DeskTicketId = ticket,
				Status = FinancialHistoryStatus.Pending,
				TimeCreated = DateTime.UtcNow,
				User = user,
				Comment = "" // see below
			};

			// add and save
			DbContext.FinancialHistory.Add(finHistory);
			await DbContext.SaveChangesAsync();

			// request
			var buyRequest = new BuyRequest() {
				User = user,
				Status = ExchangeRequestStatus.Initial,
				Currency = currency,
				FiatAmountCents = amountCents,
				Address = model.EthAddress,
				FixedRateCents = goldRate,
				DeskTicketId = ticket,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow,
				RefFinancialHistoryId = finHistory.Id,
			};

			// add and save
			DbContext.BuyRequest.Add(buyRequest);
			await DbContext.SaveChangesAsync();
			DbContext.Detach(buyRequest);

			// update comment
			finHistory.Comment = $"Buying order #{buyRequest.Id} of {CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.ResultGold, true)} GOLD";
			await DbContext.SaveChangesAsync();
			DbContext.Detach(finHistory);

			// activity
			await CoreLogic.UserAccount.SaveActivity(
				services: HttpContext.RequestServices,
				user: user,
				type: Common.UserActivityType.Exchange,
				comment: $"GOLD buying request #{buyRequest.Id} ({TextFormatter.FormatAmount(amountCents, currency)}) from {Common.TextFormatter.MaskEthereumAddress(model.EthAddress)} initiated",
				ip: agent.Ip,
				agent: agent.Agent
			);

			return APIResponse.Success(
				new BuyRequestView() {
					GoldAmount = CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.ResultGold, true),
					GoldRate = goldRate / 100d,
					Payload = new[] { user.UserName, buyRequest.Id.ToString() },
				}
			);
		}

		/// <summary>
		/// Selling request
		/// </summary>
		[AreaAuthorized]
		[HttpPost, Route("sellRequest")]
		[ProducesResponseType(typeof(SellRequestView), 200)]
		public async Task<APIResponse> SellRequest([FromBody] SellRequestModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			var user = await GetUserFromDb();
			var agent = GetUserAgentInfo();

			// ---

			var amountWei = CoreLogic.Finance.Tokens.GoldToken.ToWei((decimal)model.Amount);

			var currency = FiatCurrency.USD;
			var goldBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserGoldBalance(model.EthAddress);
			var mntpBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserMntpBalance(model.EthAddress);
			var goldRate = await GoldRateProvider.GetGoldRate(currency);

			// estimate
			var estimated = await CoreLogic.Finance.Tokens.GoldToken.EstimateSelling(
				goldAmountWei: amountWei,
				goldTotalVolumeWei: goldBalance,
				pricePerGoldOunceCents: goldRate,
				mntpBalance: mntpBalance
			);

			// ---

			// invalid amount passed
			if (estimated.InputUsed != amountWei) {
				return APIResponse.BadRequest(nameof(model.Amount), "Amount is invalid");
			}

			var ticket = await TicketDesk.CreateGoldSellingTicket(TicketStatus.Opened, user.UserName, "New gold selling request generated");

			// history
			var finHistory = new DAL.Models.FinancialHistory() {
				Type = FinancialHistoryType.GoldSelling,
				AmountCents = estimated.ResultGrossCents,
				FeeCents = estimated.ResultFeeCents,
				Currency = currency,
				DeskTicketId = ticket,
				Status = FinancialHistoryStatus.Pending,
				TimeCreated = DateTime.UtcNow,
				User = user,
				Comment = "" // see below
			};

			// add and save
			DbContext.FinancialHistory.Add(finHistory);
			await DbContext.SaveChangesAsync();

			// request
			var sellRequest = new SellRequest() {
				User = user,
				Status = ExchangeRequestStatus.Initial,
				Currency = currency,
				FiatAmountCents = estimated.ResultGrossCents,
				Address = model.EthAddress,
				FixedRateCents = goldRate,
				DeskTicketId = ticket,
				TimeCreated = DateTime.UtcNow,
				TimeNextCheck = DateTime.UtcNow,
				RefFinancialHistoryId = finHistory.Id,
			};

			// add and save
			DbContext.SellRequest.Add(sellRequest);
			await DbContext.SaveChangesAsync();
			DbContext.Detach(sellRequest);

			// update comment
			finHistory.Comment = $"Selling order #{sellRequest.Id} of {CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(amountWei, true)} GOLD";
			await DbContext.SaveChangesAsync();
			DbContext.Detach(finHistory);

			// activity
			await CoreLogic.UserAccount.SaveActivity(
				services: HttpContext.RequestServices,
				user: user,
				type: Common.UserActivityType.Exchange,
				comment: $"GOLD selling request #{sellRequest.Id} ({TextFormatter.FormatAmount(estimated.ResultGrossCents, currency)}) from {Common.TextFormatter.MaskEthereumAddress(model.EthAddress)} initiated",
				ip: agent.Ip,
				agent: agent.Agent
			);

			return APIResponse.Success(
				new SellRequestView() {
					FiatAmount = estimated.ResultGrossCents / 100d,
					GoldRate = goldRate / 100d,
					Payload = new[] { user.UserName, sellRequest.Id.ToString() },
				}
			);
		}

		// ---

		/// <summary>
		/// Buying request, dry run with estimation
		/// </summary>
		[AreaAuthorized]
		[HttpPost, Route("buyRequestDry")]
		[ProducesResponseType(typeof(BuyRequestDryView), 200)]
		public async Task<APIResponse> BuyRequestDry([FromBody] BuyRequestDryModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			// round cents
			var amountCents = (long)Math.Floor(model.Amount * 100d);
			model.Amount = amountCents / 100d;

			var user = await GetUserFromDb();

			// ---

			// TODO: get values from client request, not from eth

			var currency = FiatCurrency.USD;
			var fiatBalance = await EthereumObserver.GetUserFiatBalance(user.UserName, currency);
			var mntpBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserMntpBalance(model.EthAddress);
			var goldRate = await GoldRateCached.GetGoldRate(currency);

			// estimate
			var estimated = await CoreLogic.Finance.Tokens.GoldToken.EstimateBuying(
				fiatAmountCents: amountCents,
				fiatTotalVolumeCents: fiatBalance,
				pricePerGoldOunceCents: goldRate,
				mntpBalance: mntpBalance
			);

			return APIResponse.Success(
				new BuyRequestDryView() {
					AmountUsed = estimated.InputUsed / 100d,
					AmountMin = estimated.InputMin / 100d,
					AmountMax = estimated.InputMax / 100d,
					GoldAmount = CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.ResultGold, true),
					GoldRate = goldRate / 100d,
					Fee = estimated.ResultFeeCents / 100d,
				}
			);
		}

		/// <summary>
		/// Selling request, dry run with estimation
		/// </summary>
		[AreaAuthorized]
		[HttpPost, Route("sellRequestDry")]
		[ProducesResponseType(typeof(SellRequestDryView), 200)]
		public async Task<APIResponse> SellRequestDry([FromBody] SellRequestDryModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			var user = await GetUserFromDb();

			// ---

			// TODO: get values from client request, not from eth

			var currency = FiatCurrency.USD;
			var amountWei = CoreLogic.Finance.Tokens.GoldToken.ToWei((decimal)model.Amount);
			var goldBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserGoldBalance(model.EthAddress);
			var mntpBalance = model.EthAddress == null ? BigInteger.Zero : await EthereumObserver.GetUserMntpBalance(model.EthAddress);
			var goldRate = await GoldRateProvider.GetGoldRate(currency);

			// estimate
			var estimated = await CoreLogic.Finance.Tokens.GoldToken.EstimateSelling(
				goldAmountWei: amountWei,
				goldTotalVolumeWei: goldBalance,
				pricePerGoldOunceCents: goldRate,
				mntpBalance: mntpBalance
			);

			return APIResponse.Success(
				new SellRequestDryView() {
					AmountUsed = CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.InputUsed, true),
					AmountMin = CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.InputMin, true),
					AmountMax = CoreLogic.Finance.Tokens.GoldToken.FromWeiFixed(estimated.InputMax, false),
					FiatAmount = estimated.ResultGrossCents / 100d,
					GoldRate = goldRate / 100d,
					Fee = estimated.ResultFeeCents / 100d,
				}
			);
		}

	}
}
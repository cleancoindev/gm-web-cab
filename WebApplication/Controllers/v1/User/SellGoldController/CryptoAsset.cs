﻿using Goldmint.Common;
using Goldmint.WebApplication.Core.Policies;
using Goldmint.WebApplication.Core.Response;
using Goldmint.WebApplication.Models.API;
using Goldmint.WebApplication.Models.API.v1.User.SellGoldModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Goldmint.Common.Extensions;

namespace Goldmint.WebApplication.Controllers.v1.User {

	public partial class SellGoldController : BaseController {

		[RequireJWTAudience(JwtAudience.Cabinet), RequireJWTArea(JwtArea.Authorized)]
		[HttpPost, Route("asset/eth")]
		[ProducesResponseType(typeof(AssetEthView), 200)]
		public async Task<APIResponse> AssetEth([FromBody] AssetEthModel model) {

			// validate
			if (BaseValidableModel.IsInvalid(model, out var errFields)) {
				return APIResponse.BadRequest(errFields);
			}

			// try parse amount
			if (!BigInteger.TryParse(model.Amount, out var inputAmount) || inputAmount < 1) {
				return APIResponse.BadRequest(nameof(model.Amount), "Invalid amount");
			}

			// try parse fiat currency
			var exchangeCurrency = FiatCurrency.Usd;
			if (Enum.TryParse(model.Currency, true, out FiatCurrency fc)) {
				exchangeCurrency = fc;
			}

			// ---

			var rcfg = RuntimeConfigHolder.Clone();

			var user = await GetUserFromDb();
			var userTier = CoreLogic.User.GetTier(user);
			var agent = GetUserAgentInfo();

			if (userTier < UserTier.Tier2) {
				return APIResponse.BadRequest(APIErrorCode.AccountNotVerified);
			}

			// ---

			if (!rcfg.Gold.AllowTradingEth) {
				return APIResponse.BadRequest(APIErrorCode.TradingNotAllowed);
			}

			var limits = WithdrawalLimits(rcfg, TradableCurrency.Eth);

			var estimation = await Estimation(rcfg, inputAmount, TradableCurrency.Eth, exchangeCurrency, model.EthAddress, model.Reversed, limits.Min, limits.Max);
			if (!estimation.TradingAllowed || estimation.ResultCurrencyAmount < 1) {
				return APIResponse.BadRequest(APIErrorCode.TradingNotAllowed);
			}
			if (estimation.IsLimitExceeded) {
				return APIResponse.BadRequest(APIErrorCode.TradingExchangeLimit, estimation.View.Limits);
			}

			// limit gold amount to max available
			if (estimation.ResultGoldAmount.FromSumus() > user.UserSumusWallet.BalanceGold) {
				estimation = await Estimation(rcfg, user.UserSumusWallet.BalanceGold.ToSumus(), TradableCurrency.Eth, exchangeCurrency, model.EthAddress, false, limits.Min, limits.Max);
				if (!estimation.TradingAllowed || estimation.ResultCurrencyAmount < 1) {
					return APIResponse.BadRequest(APIErrorCode.TradingNotAllowed);
				}
				if (estimation.IsLimitExceeded) {
					return APIResponse.BadRequest(APIErrorCode.TradingExchangeLimit, estimation.View.Limits);
				}
			}

			var timeNow = DateTime.UtcNow;

			// history
			var finHistory = new DAL.Models.UserFinHistory() {
				Status = UserFinHistoryStatus.Unconfirmed,
				Type = UserFinHistoryType.GoldSell,
				Source = "GOLD",
				SourceAmount = TextFormatter.FormatTokenAmountFixed(estimation.ResultGoldAmount, TokensPrecision.Sumus),
				Destination = "ETH",
				DestinationAmount = TextFormatter.FormatTokenAmountFixed(estimation.ResultCurrencyAmount, TokensPrecision.Ethereum),
				Comment = "",
				TimeCreated = timeNow,
				UserId = user.Id,
			};
			DbContext.UserFinHistory.Add(finHistory);
			await DbContext.SaveChangesAsync();

			// request
			var request = new DAL.Models.SellGoldEth() {
				Status = BuySellGoldRequestStatus.Unconfirmed,
				GoldAmount = estimation.ResultGoldAmount.FromSumus(),
				Destination = model.EthAddress,
				EthAmount = estimation.ResultCurrencyAmount.FromSumus(),
				ExchangeCurrency = exchangeCurrency,
				GoldRateCents = estimation.CentsPerGoldRate,
				EthRateCents = estimation.CentsPerAssetRate,
				TimeCreated = timeNow,
				RelFinHistoryId = finHistory.Id,
				UserId = user.Id,
			};

			// add and save
			DbContext.SellGoldEth.Add(request);
			await DbContext.SaveChangesAsync();

			var assetPerGold = CoreLogic.Finance.Estimation.AssetPerGold(TradableCurrency.Eth, estimation.CentsPerAssetRate, estimation.CentsPerGoldRate);

			return APIResponse.Success(
				new AssetEthView() {
					RequestId = request.Id,
					EthRate = estimation.CentsPerAssetRate / 100d,
					GoldRate = estimation.CentsPerGoldRate / 100d,
					Currency = exchangeCurrency.ToString().ToUpper(),
					EthPerGoldRate = assetPerGold.ToString(),
					Estimation = estimation.View,
				}
			);
		}
	}
}
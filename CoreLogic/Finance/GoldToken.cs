﻿using Goldmint.Common;
using Goldmint.CoreLogic.Services.Blockchain;
using Goldmint.CoreLogic.Services.Mutex;
using Goldmint.CoreLogic.Services.Mutex.Impl;
using Goldmint.CoreLogic.Services.Rate;
using Goldmint.CoreLogic.Services.RuntimeConfig.Impl;
using Goldmint.CoreLogic.Services.Oplog;
using Goldmint.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Goldmint.DAL.Models;

namespace Goldmint.CoreLogic.Finance {

	public static class GoldToken {

		/// <summary>
		/// Process ETH to GOLD buying request (core-worker)
		/// ETH received at contract, GOLD will be issued
		/// </summary>
		public static async Task<BuySellRequestProcessingResult> OnEthereumContractBuyEvent(IServiceProvider services, BigInteger requestIndex, long internalRequestId, string address, BigInteger amountEth, string txId, int txConfirmationsRequired) {

			if (internalRequestId <= 0) return BuySellRequestProcessingResult.InvalidArgs;
			if (string.IsNullOrWhiteSpace(address)) return BuySellRequestProcessingResult.InvalidArgs;
			if (amountEth < 0) return BuySellRequestProcessingResult.InvalidArgs;
			if (string.IsNullOrWhiteSpace(txId)) return BuySellRequestProcessingResult.InvalidArgs;

			var logger = services.GetLoggerFor(typeof(GoldToken));
			var appConfig = services.GetRequiredService<AppConfig>();
			var runtimeConfig = services.GetRequiredService<RuntimeConfigHolder>().Clone();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();
			var safeRates = services.GetRequiredService<IAggregatedSafeRatesSource>();
			var ethereumReader = services.GetRequiredService<IEthereumReader>();

			var query =
				from r in dbContext.BuyGoldRequest
				where
					r.Input == BuyGoldRequestInput.ContractEthPayment &&
					r.Id == internalRequestId &&
					r.Status == BuyGoldRequestStatus.Confirmed &&
					r.Output == BuyGoldRequestOutput.EthereumAddress &&
					r.EthAddress == address
				select r
			;

			// find first
			if (await (query).AsNoTracking().CountAsync() != 1) {
				return BuySellRequestProcessingResult.NotFound;
			}

			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.GoldBuyingReq, internalRequestId)
			;

			return await mutexBuilder.CriticalSection<BuySellRequestProcessingResult>(async (ok) => {
				if (ok) {

					// get again
					var request = await (query).Include(_ => _.RefUserFinHistory).FirstOrDefaultAsync();
					if (request == null) {
						return BuySellRequestProcessingResult.NotFound;
					}

					try {
						await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"User's Ethereum transaction of { TextFormatter.FormatTokenAmount(amountEth, Common.Tokens.ETH.Decimals) } ETH is {txId}");
					}
					catch { }

					try {

						// get tx info
						var txInfo = await ethereumReader.CheckTransaction(txId, txConfirmationsRequired);
						if (txInfo.Status != EthTransactionStatus.Success || txInfo.Time == null) {
							return BuySellRequestProcessingResult.InvalidArgs;
						}

						var timeNow = DateTime.UtcNow;

						// ok
						if (request.TimeExpires > txInfo.Time.Value) {

							var ethPerGoldFixedRate = Estimation.AssetPerGold(CryptoCurrency.Eth, request.InputRateCents, request.GoldRateCents);
							var ethActualRate = safeRates.GetRate(CurrencyRateType.Eth);
							var goldActualRate = safeRates.GetRate(CurrencyRateType.Gold);

							var cancelRequest =
								ethPerGoldFixedRate <= 0 || !ethActualRate.CanSell || !goldActualRate.CanBuy ||
								Estimation.IsFixedRateThresholdExceeded(request.InputRateCents, ethActualRate.Usd, runtimeConfig.Gold.SafeRate.Eth.SellEthGoldChangeThreshold) ||
								Estimation.IsFixedRateThresholdExceeded(request.GoldRateCents, goldActualRate.Usd, runtimeConfig.Gold.SafeRate.Gold.BuyEthGoldChangeThreshold)
							;

							if (cancelRequest) {
								try {
									await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"Request cancelled internally due to significant currencies rate change");
								}
								catch { }
							}

							// estimated gold amount
							var estimatedGoldAmount = await Estimation.BuyGoldCrypto(
								services: services,
								cryptoCurrency: CryptoCurrency.Eth,
								fiatCurrency: request.ExchangeCurrency,
								cryptoAmount: amountEth,
								knownGoldRateCents: request.GoldRateCents,
								knownCryptoRateCents: request.InputRateCents
							);

							// eth operation
							var ethOp = new DAL.Models.EthereumOperation() {
								Type = cancelRequest? EthereumOperationType.ContractCancelBuyRequest: EthereumOperationType.ContractProcessBuyRequest,
								Status = EthereumOperationStatus.Initial,
								RelatedExchangeRequestId = request.Id,

								DestinationAddress = request.EthAddress,
								Rate = ethPerGoldFixedRate.ToString(),
								GoldAmount = estimatedGoldAmount.ResultGoldAmount.ToString(),
								EthRequestIndex = requestIndex.ToString(),
								OplogId = request.OplogId,
								TimeCreated = timeNow,
								TimeNextCheck = timeNow,

								UserId = request.UserId,
								RefUserFinHistoryId = request.RefUserFinHistoryId,
							};
							dbContext.EthereumOperation.Add(ethOp);
							await dbContext.SaveChangesAsync();

							// done
							ethOp.Status = EthereumOperationStatus.Prepared;
							request.Status = cancelRequest? BuyGoldRequestStatus.Cancelled: BuyGoldRequestStatus.Success;
							request.TimeNextCheck = timeNow;
							request.TimeCompleted = timeNow;
							request.RefUserFinHistory.Status = cancelRequest? UserFinHistoryStatus.Failed: UserFinHistoryStatus.Completed;
							request.RefUserFinHistory.TimeCompleted = timeNow;
							request.RefUserFinHistory.SourceAmount = TextFormatter.FormatTokenAmountFixed(amountEth, Tokens.ETH.Decimals);
							request.RefUserFinHistory.DestinationAmount = TextFormatter.FormatTokenAmountFixed(estimatedGoldAmount.ResultGoldAmount, Tokens.GOLD.Decimals);
							await dbContext.SaveChangesAsync();

							try {
								await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"Request #{request.Id} processed. Ethereum operation #{ ethOp.Id } enqueued");
							}
							catch {
							}

							return cancelRequest? BuySellRequestProcessingResult.Cancelled: BuySellRequestProcessingResult.Success;
						}

						// expired
						else {

							request.Status = BuyGoldRequestStatus.Expired;
							request.TimeNextCheck = timeNow;
							request.TimeCompleted = timeNow;
							request.RefUserFinHistory.Status = UserFinHistoryStatus.Failed;
							request.RefUserFinHistory.TimeCompleted = timeNow;
							await dbContext.SaveChangesAsync();

							try {
								await ticketDesk.Update(request.OplogId, UserOpLogStatus.Failed, $"Request #{request.Id} is expired");
							}
							catch {
							}

							return BuySellRequestProcessingResult.Expired;
						}
					}
					catch (Exception e) {
						logger.Error(e, $"Failed to process buy request #{request.Id}");
					}
				}
				return BuySellRequestProcessingResult.MutexFailure;
			});
		}

		/// <summary>
		/// Process contract GOLD selling request (core-worker harvester)
		/// GOLD burnt at contract, ETH/fiat will be sent
		/// </summary>
		public static async Task<BuySellRequestProcessingResult> OnEthereumContractSellEvent(IServiceProvider services, BigInteger requestIndex, long internalRequestId, string address, BigInteger amountGold, string txId, int txConfirmationsRequired) {

			if (internalRequestId <= 0) return BuySellRequestProcessingResult.InvalidArgs;
			if (string.IsNullOrWhiteSpace(address)) return BuySellRequestProcessingResult.InvalidArgs;
			if (amountGold < 0) return BuySellRequestProcessingResult.InvalidArgs;
			if (string.IsNullOrWhiteSpace(txId)) return BuySellRequestProcessingResult.InvalidArgs;

			var logger = services.GetLoggerFor(typeof(GoldToken));
			var appConfig = services.GetRequiredService<AppConfig>();
			var runtimeConfig = services.GetRequiredService<RuntimeConfigHolder>().Clone();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();
			var safeRates = services.GetRequiredService<IAggregatedSafeRatesSource>();
			var ethereumReader = services.GetRequiredService<IEthereumReader>();

			var query =
				from r in dbContext.SellGoldRequest
				where
					r.Input == SellGoldRequestInput.ContractGoldBurning &&
					r.Id == internalRequestId &&
					r.Status == SellGoldRequestStatus.Confirmed &&
					r.Output == SellGoldRequestOutput.EthAddress &&
					r.EthAddress == address
				select r
			;

			// find first
			if (await (query).AsNoTracking().CountAsync() != 1) {
				return BuySellRequestProcessingResult.NotFound;
			}

			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.GoldSellingReq, internalRequestId)
			;

			return await mutexBuilder.CriticalSection<BuySellRequestProcessingResult>(async (ok) => {
				if (ok) {

					// get again
					var request = await (query).Include(_ => _.RefUserFinHistory).FirstOrDefaultAsync();
					if (request == null) {
						return BuySellRequestProcessingResult.NotFound;
					}

					try {
						await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"User's Ethereum transaction of { TextFormatter.FormatTokenAmount(amountGold, Common.Tokens.GOLD.Decimals) } GOLD is {txId}");
					}
					catch { }

					try {

						// get tx info
						var txInfo = await ethereumReader.CheckTransaction(txId, txConfirmationsRequired);
						if (txInfo.Status != EthTransactionStatus.Success || txInfo.Time == null) {
							return BuySellRequestProcessingResult.InvalidArgs;
						}

						var timeNow = DateTime.UtcNow;

						// ETH
						if (request.Output == SellGoldRequestOutput.EthAddress) {

							// ok
							if (request.TimeExpires > txInfo.Time.Value) {

								var ethPerGoldFixedRate =
									Estimation.AssetPerGold(CryptoCurrency.Eth, request.OutputRateCents, request.GoldRateCents);
								var ethActualRate = safeRates.GetRate(CurrencyRateType.Eth);
								var goldActualRate = safeRates.GetRate(CurrencyRateType.Gold);

								var cancelRequest =
										ethPerGoldFixedRate <= 0 || !ethActualRate.CanBuy || !goldActualRate.CanSell ||
										Estimation.IsFixedRateThresholdExceeded(request.OutputRateCents, ethActualRate.Usd,
											runtimeConfig.Gold.SafeRate.Eth.BuyEthGoldChangeThreshold) ||
										Estimation.IsFixedRateThresholdExceeded(request.GoldRateCents, goldActualRate.Usd,
											runtimeConfig.Gold.SafeRate.Gold.SellEthGoldChangeThreshold)
									;

								if (cancelRequest) {
									try {
										await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending,
											$"Request cancelled internally due to significant currencies rate change");
									}
									catch {
									}
								}

								// estimated crypto amount
								var estimatedCryptoAmount = await Estimation.SellGoldCrypto(
									services: services,
									cryptoCurrency: CryptoCurrency.Eth,
									fiatCurrency: request.ExchangeCurrency,
									goldAmount: amountGold,
									knownGoldRateCents: request.GoldRateCents,
									knownCryptoRateCents: request.OutputRateCents
								);
								var estimatedCryptoAmountFee = Estimation.SellingFeeForCrypto(
									CryptoCurrency.Eth, estimatedCryptoAmount.ResultAssetAmount
								);

								// eth operation
								var ethOp = new DAL.Models.EthereumOperation() {
									Type = cancelRequest
										? EthereumOperationType.ContractCancelSellRequest
										: EthereumOperationType.ContractProcessSellRequest,
									Status = EthereumOperationStatus.Initial,
									RelatedExchangeRequestId = request.Id,

									DestinationAddress = request.EthAddress,
									Rate = ethPerGoldFixedRate.ToString(),
									GoldAmount = amountGold.ToString(),
									EthRequestIndex = requestIndex.ToString(),
									OplogId = request.OplogId,
									TimeCreated = timeNow,
									TimeNextCheck = timeNow,

									UserId = request.UserId,
									RefUserFinHistoryId = request.RefUserFinHistoryId,
								};
								dbContext.EthereumOperation.Add(ethOp);
								await dbContext.SaveChangesAsync();

								// done
								ethOp.Status = EthereumOperationStatus.Prepared;
								request.Status = cancelRequest ? SellGoldRequestStatus.Cancelled : SellGoldRequestStatus.Success;
								request.TimeNextCheck = timeNow;
								request.TimeCompleted = timeNow;
								request.RefUserFinHistory.Status = cancelRequest ? UserFinHistoryStatus.Failed : UserFinHistoryStatus.Completed;
								request.RefUserFinHistory.TimeCompleted = timeNow;
								request.RefUserFinHistory.SourceAmount = TextFormatter.FormatTokenAmountFixed(amountGold, Tokens.GOLD.Decimals);
								request.RefUserFinHistory.DestinationAmount =
									TextFormatter.FormatTokenAmountFixed(estimatedCryptoAmount.ResultAssetAmount - estimatedCryptoAmountFee,
										Tokens.GOLD.Decimals);
								await dbContext.SaveChangesAsync();

								try {
									await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending,
										$"Request #{request.Id} processed. Ethereum operation #{ethOp.Id} enqueued");
								}
								catch {
								}

								return cancelRequest ? BuySellRequestProcessingResult.Cancelled : BuySellRequestProcessingResult.Success;
							}

							// expired
							else {

								request.Status = SellGoldRequestStatus.Expired;
								request.TimeNextCheck = timeNow;
								request.TimeCompleted = timeNow;
								request.RefUserFinHistory.Status = UserFinHistoryStatus.Failed;
								request.RefUserFinHistory.TimeCompleted = timeNow;
								await dbContext.SaveChangesAsync();

								try {
									await ticketDesk.Update(request.OplogId, UserOpLogStatus.Failed, $"Request #{request.Id} is expired");
								}
								catch {
								}

								return BuySellRequestProcessingResult.Expired;
							}
						}

						// credit card
						if (request.Output == SellGoldRequestOutput.CreditCard) {
							
							// ok
							if (request.TimeExpires > txInfo.Time.Value) {

								var card = (UserCreditCard) null;
								var payment = (CreditCardPayment) null;
								var ethOp = (DAL.Models.EthereumOperation) null;

								var cancelRequest = true;
								var amountCents = 0L;
								// ! call contract processFiatSellRequest() and get amount of cents
								// ! check cents for 0 - continue or cancelRequest = true

								// card
								if (request.RelOutputId != null) {
									card = await (
										from c in dbContext.UserCreditCard
										where 
											c.Id == request.RelOutputId.Value &&
											c.UserId == request.Id
										select c
									)
										.AsNoTracking()
										.FirstOrDefaultAsync()
									;
								}
								else {
									cancelRequest = true;
								}

								// eth operation - cancel
								if (cancelRequest) {
									ethOp = new DAL.Models.EthereumOperation() {
										Type = EthereumOperationType.ContractCancelSellRequest,
										Status = EthereumOperationStatus.Initial,
										RelatedExchangeRequestId = request.Id,

										DestinationAddress = request.EthAddress,
										Rate = "0",
										GoldAmount = amountGold.ToString(),
										EthRequestIndex = requestIndex.ToString(),
										OplogId = request.OplogId,
										TimeCreated = timeNow,
										TimeNextCheck = timeNow,

										UserId = request.UserId,
										RefUserFinHistoryId = request.RefUserFinHistoryId,
									};
									dbContext.EthereumOperation.Add(ethOp);
									await dbContext.SaveChangesAsync();

									try {
										await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"Request #{request.Id} cancelled. Ethereum operation #{ ethOp.Id } enqueued");
									}
									catch { }

									ethOp.Status = EthereumOperationStatus.Prepared;
								}
								// withdrawal op
								else {
									payment = await The1StPaymentsProcessing.CreateWithdrawPayment(
										services: services,
										card: card,
										currency: request.ExchangeCurrency,
										amountCents: amountCents,
										sellRequestId: request.Id,
										oplogId: request.OplogId
									);
									dbContext.CreditCardPayment.Add(payment);
									await dbContext.SaveChangesAsync();

									try {
										await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"Request #{request.Id} processed. Withdrawal payment #{ payment.Id } enqueued");
									}
									catch { }

									payment.Status = CardPaymentStatus.Pending;
								}

								// done
								request.Status = cancelRequest ? SellGoldRequestStatus.Cancelled : SellGoldRequestStatus.Success;
								request.TimeNextCheck = timeNow;
								request.TimeCompleted = timeNow;
								request.RefUserFinHistory.Status = cancelRequest ? UserFinHistoryStatus.Failed : UserFinHistoryStatus.Completed;
								request.RefUserFinHistory.TimeCompleted = timeNow;
								request.RefUserFinHistory.SourceAmount = TextFormatter.FormatTokenAmountFixed(amountGold, Tokens.GOLD.Decimals);
								request.RefUserFinHistory.DestinationAmount = TextFormatter.FormatAmount(amountCents, request.ExchangeCurrency);
								await dbContext.SaveChangesAsync();

								return cancelRequest ? BuySellRequestProcessingResult.Cancelled : BuySellRequestProcessingResult.Success;
							}

							// expired
							else {

								request.Status = SellGoldRequestStatus.Expired;
								request.TimeNextCheck = timeNow;
								request.TimeCompleted = timeNow;
								request.RefUserFinHistory.Status = UserFinHistoryStatus.Failed;
								request.RefUserFinHistory.TimeCompleted = timeNow;
								await dbContext.SaveChangesAsync();

								try {
									await ticketDesk.Update(request.OplogId, UserOpLogStatus.Failed, $"Request #{request.Id} is expired");
								}
								catch {
								}

								return BuySellRequestProcessingResult.Expired;
							}
						}
					}
					catch (Exception e) {
						logger.Error(e, $"Failed to process sell request #{request.Id}");
					}
				}
				return BuySellRequestProcessingResult.MutexFailure;
			});
		}

		/// <summary>
		/// Process CreditCard-deposit to GOLD buying request (core-worker)
		/// Fiat received, GOLD will be issued
		/// </summary>
		public static async Task<BuySellRequestProcessingResult> OnCreditCardDepositCompleted(IServiceProvider services, long requestId, CreditCardPayment payment) {

			if (payment == null || payment.Type != CardPaymentType.Deposit) return BuySellRequestProcessingResult.InvalidArgs;
			if (requestId <= 0) return BuySellRequestProcessingResult.InvalidArgs;
			if (payment.AmountCents <= 0) return BuySellRequestProcessingResult.InvalidArgs;

			var logger = services.GetLoggerFor(typeof(GoldToken));
			var appConfig = services.GetRequiredService<AppConfig>();
			var runtimeConfig = services.GetRequiredService<RuntimeConfigHolder>().Clone();
			var mutexHolder = services.GetRequiredService<IMutexHolder>();
			var dbContext = services.GetRequiredService<ApplicationDbContext>();
			var ticketDesk = services.GetRequiredService<IOplogProvider>();
			var safeRates = services.GetRequiredService<IAggregatedSafeRatesSource>();
			var ethereumReader = services.GetRequiredService<IEthereumReader>();

			var query =
				from r in dbContext.BuyGoldRequest
				where
					r.Input == BuyGoldRequestInput.CreditCardDeposit &&
					r.Id == requestId &&
					r.Status == BuyGoldRequestStatus.Confirmed &&
					r.Output == BuyGoldRequestOutput.EthereumAddress
				select r
			;

			// find first
			if (await (query).AsNoTracking().CountAsync() != 1) {
				return BuySellRequestProcessingResult.NotFound;
			}

			var mutexBuilder =
				new MutexBuilder(mutexHolder)
				.Mutex(MutexEntity.GoldBuyingReq, requestId)
			;

			return await mutexBuilder.CriticalSection<BuySellRequestProcessingResult>(async (ok) => {
				if (ok) {

					// get again
					var request = await (query).Include(_ => _.RefUserFinHistory).FirstOrDefaultAsync();
					if (request == null) {
						return BuySellRequestProcessingResult.NotFound;
					}

					try {

						if (payment.Status != CardPaymentStatus.Success && payment.Status != CardPaymentStatus.Failed) {
							return BuySellRequestProcessingResult.InvalidArgs;
						}

						var timeNow = DateTime.UtcNow;

						// ok
						if (payment.Status == CardPaymentStatus.Success) {

							// estimated gold amount
							var estimatedGoldAmount = await Estimation.BuyGoldFiat(
								services: services,
								fiatCurrency: request.ExchangeCurrency,
								fiatAmountCents: payment.AmountCents,
								knownGoldRateCents: request.GoldRateCents
							);

							// eth operation
							// ! var ethOp = new DAL.Models.EthereumOperation() {
							// ! 	Type = ,
							// ! 	Status = EthereumOperationStatus.Initial,
							// ! 	RelatedExchangeRequestId = request.Id,
							// ! 
							// ! 	DestinationAddress = request.EthAddress,
							// ! 	Rate = ethPerGoldFixedRate.ToString(),
							// ! 	GoldAmount = estimatedGoldAmount.ResultGoldAmount.ToString(),
							// ! 	EthRequestIndex = requestIndex.ToString(),
							// ! 	OplogId = request.OplogId,
							// ! 	TimeCreated = timeNow,
							// ! 	TimeNextCheck = timeNow,
							// ! 
							// ! 	UserId = request.UserId,
							// ! 	RefUserFinHistoryId = request.RefUserFinHistoryId,
							// ! };
							// ! dbContext.EthereumOperation.Add(ethOp);
							// ! await dbContext.SaveChangesAsync();

							// done
							// ! ethOp.Status = EthereumOperationStatus.Prepared;
							request.Status = BuyGoldRequestStatus.Success;
							request.TimeNextCheck = timeNow;
							request.TimeCompleted = timeNow;
							request.RefUserFinHistory.Status = UserFinHistoryStatus.Completed;
							request.RefUserFinHistory.TimeCompleted = timeNow;
							request.RefUserFinHistory.SourceAmount = TextFormatter.FormatAmount(payment.AmountCents, payment.Currency);
							request.RefUserFinHistory.DestinationAmount = TextFormatter.FormatTokenAmountFixed(estimatedGoldAmount.ResultGoldAmount, Tokens.GOLD.Decimals);
							await dbContext.SaveChangesAsync();

							// ! try {
							// ! 	await ticketDesk.Update(request.OplogId, UserOpLogStatus.Pending, $"Request #{request.Id} processed. Ethereum operation #{ethOp.Id} enqueued");
							// ! }
							// ! catch {
							// ! }

							return BuySellRequestProcessingResult.Success;
						}
						// failed
						else {

							request.Status = BuyGoldRequestStatus.Failed;
							request.TimeNextCheck = timeNow;
							request.TimeCompleted = timeNow;
							request.RefUserFinHistory.Status = UserFinHistoryStatus.Failed;
							request.RefUserFinHistory.TimeCompleted = timeNow;
							await dbContext.SaveChangesAsync();

							return BuySellRequestProcessingResult.Cancelled;
						}
					}
					catch (Exception e) {
						logger.Error(e, $"Failed to process buy request #{request.Id}");
					}
				}
				return BuySellRequestProcessingResult.MutexFailure;
			});
		}

		// ---

		public enum BuySellRequestProcessingResult {
			Cancelled,
			Success,
			Expired,
			InvalidArgs,
			NotFound,
			MutexFailure,
		}
	}
}

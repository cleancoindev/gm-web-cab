using Goldmint.Common;
using Goldmint.CoreLogic.Services.Rate;
using Goldmint.CoreLogic.Services.Rate.Impl;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace Goldmint.CoreLogicTests.Estimation {

	public sealed class Estimation : Test {

		private readonly ServiceProvider _services;
		private SafeRatesDispatcher _ratesDispatcher;
		private DebugRateProvider _ratesProvider;

		public Estimation(ITestOutputHelper testOutput) : base(testOutput) {

			var services = new ServiceCollection();
			SetupServices(services);
			_services = services.BuildServiceProvider();
		}

		private void SetupServices(ServiceCollection services) {
			services.AddSingleton<SafeRatesFiatAdapter>();

			_ratesProvider = new DebugRateProvider();
			services.AddSingleton<IGoldRateProvider>(_ratesProvider);
			services.AddSingleton<IEthRateProvider>(_ratesProvider);

			_ratesDispatcher = new SafeRatesDispatcher(
				null,
				RuntimeConfigHolder,
				LogFactory,
				opts => {
					opts.PublishPeriod = TimeSpan.FromSeconds(1);
					opts.GoldTtl = TimeSpan.FromSeconds(60);
					opts.EthTtl = TimeSpan.FromSeconds(60);
				}
			);
			_ratesDispatcher.Run();
			services.AddSingleton<IAggregatedSafeRatesSource>(_ratesDispatcher);
		}

		protected override void DisposeManaged() {
			_ratesDispatcher?.Dispose();
			base.DisposeManaged();
		}

		// ---

		[Fact]
		public void AssetPerGold() {
			Assert.True(CoreLogic.Finance.Estimation.AssetPerGold(EthereumToken.Eth, 100000, 100000).ToString() == "1000000000000000000");
			Assert.True(CoreLogic.Finance.Estimation.AssetPerGold(EthereumToken.Eth, 50000, 100000).ToString() == "2000000000000000000");
			Assert.True(CoreLogic.Finance.Estimation.AssetPerGold(EthereumToken.Eth, 300000, 100000).ToString() == "333333333333333333");
			Assert.True(CoreLogic.Finance.Estimation.AssetPerGold(EthereumToken.Eth, 30000, 100000).ToString() == "3333333333333333333");
		}

		[Fact]
		public void IsFixedRateThresholdExceeded() {
			Assert.False(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(3333, 3666, 0.1d));
			Assert.False(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(3666, 3333, 0.1d));
			Assert.True(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(3666, 3200, 0.1d));
			Assert.True(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(3200, 3666, 0.1d));
			Assert.False(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(100000, 120000, 0.2d));
			Assert.True(CoreLogic.Finance.Estimation.IsFixedRateThresholdExceeded(100000, 80000, 0.15d));
		}

		[Fact]
		public void BuyGoldSimpleEstimation() {

			var gRate = 140000;
			var eRate = 70000;

			_ratesProvider.SetSpread(0d);
			_ratesProvider.SetGoldRate(gRate);
			_ratesProvider.SetEthRate(eRate);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			// crypto
			var cres = CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1 * BigInteger.Pow(10, TokensPrecision.Ethereum)).Result;
			Assert.True(cres.Allowed);
			Assert.True(cres.CentsPerGoldRate == gRate);
			Assert.True(cres.CentsPerAssetRate == eRate);
			Assert.True(cres.CryptoPerGoldRate == 2 * BigInteger.Pow(10, TokensPrecision.Ethereum));
			Assert.True(cres.ResultGoldAmount == 5 * BigInteger.Pow(10, TokensPrecision.EthereumGold - 1));

			// crypto
			cres = CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 4 * BigInteger.Pow(10, TokensPrecision.Ethereum)).Result;
			Assert.True(cres.Allowed);
			Assert.True(cres.CentsPerGoldRate == gRate);
			Assert.True(cres.CentsPerAssetRate == eRate);
			Assert.True(cres.CryptoPerGoldRate == 2 * BigInteger.Pow(10, TokensPrecision.Ethereum));
			Assert.True(cres.ResultGoldAmount == 2 * BigInteger.Pow(10, TokensPrecision.EthereumGold));

			// fiat
			var fiatAmount = gRate;
			var fres = CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, fiatAmount).Result;
			Assert.True(fres.Allowed);
			Assert.True(fres.CentsPerGoldRate == gRate);
			Assert.True(fres.ResultCentsAmount == fiatAmount);
			Assert.True(fres.ResultGoldAmount == 1 * BigInteger.Pow(10, TokensPrecision.EthereumGold));

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
		}

		[Fact]
		public void SellGoldSimpleEstimation() {

			var gRate = 100000;
			var eRate = 200000;

			_ratesProvider.SetSpread(0d);
			_ratesProvider.SetGoldRate(gRate);
			_ratesProvider.SetEthRate(eRate);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			// crypto
			var goldAmount = 1 * BigInteger.Pow(10, TokensPrecision.EthereumGold);
			var res = CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, goldAmount).Result;
			Assert.True(res.Allowed);
			Assert.True(res.CentsPerGoldRate == gRate);
			Assert.True(res.CentsPerAssetRate == eRate);
			Assert.True(res.CryptoPerGoldRate == 5 * BigInteger.Pow(10, TokensPrecision.Ethereum - 1));
			Assert.True(res.ResultAssetAmount == 5 * BigInteger.Pow(10, TokensPrecision.Ethereum - 1));

			// fiat
			goldAmount = 1 * BigInteger.Pow(10, TokensPrecision.EthereumGold);
			var fres = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, goldAmount).Result;
			Assert.True(fres.Allowed);
			Assert.True(fres.CentsPerGoldRate == gRate);
			Assert.True(fres.ResultCentsAmount == gRate);

			// fiat overflow
			goldAmount = (new BigInteger(long.MaxValue) / gRate + 1) * BigInteger.Pow(10, TokensPrecision.EthereumGold);
			fres = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, goldAmount).Result;
			Assert.False(fres.Allowed);
			Assert.True(fres.Status == CoreLogic.Finance.Estimation.SellGoldStatus.ValueOverflow);
			Assert.True(fres.ResultCentsAmount == 0);

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
		}

		// ---

		[Fact]
		public void BuyGoldEstimationReversedFiat() {

			_ratesProvider.SetSpread(0d);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			var requiredGoldAmount = BigInteger.Pow(10, TokensPrecision.EthereumGold);

			var rev = CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, requiredGoldAmount).Result;
			var def = CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, rev.ResultCentsAmount).Result;

			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
			Assert.True(def.ResultGoldAmount >= requiredGoldAmount);

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
		}

		[Fact]
		public void BuyGoldEstimationReversedCrypto() {

			_ratesProvider.SetSpread(0d);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			var requiredGoldAmount = BigInteger.Pow(10, TokensPrecision.EthereumGold);

			var rev = CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, requiredGoldAmount).Result;
			var def = CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, rev.ResultAssetAmount).Result;

			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultGoldAmount >= requiredGoldAmount);

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, 0).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, -1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
		}

		[Fact]
		public void BuyGoldEstimationReversedFiatRandom() {

			for (var i = 0; i < 100000; ++i) {

				_ratesProvider.SetSpread(0.2d);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
				_ratesDispatcher.ForceUpdate().Wait();

				var requiredGoldAmount = (SecureRandom.GetPositiveInt() % 200000000) * BigInteger.Pow(10, TokensPrecision.EthereumGold - 8);

				var rev = CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, requiredGoldAmount).Result;
				var def = CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, rev.ResultCentsAmount).Result;

				Assert.True(def.Allowed && rev.Allowed);
				Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
				Assert.True(def.ResultGoldAmount >= requiredGoldAmount);
			}
		}

		[Fact]
		public void BuyGoldEstimationReversedCryptoRandom() {

			for (var i = 0; i < 100000; ++i) {

				_ratesProvider.SetSpread(0.2d);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
				_ratesDispatcher.ForceUpdate().Wait();

				var requiredGoldAmount = (SecureRandom.GetPositiveInt() % 200000000) * BigInteger.Pow(10, TokensPrecision.EthereumGold - 8);

				var rev = CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, requiredGoldAmount).Result;
				var def = CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, rev.ResultAssetAmount).Result;

				Assert.True(def.Allowed && rev.Allowed);
				Assert.True(def.ResultGoldAmount >= requiredGoldAmount);
			}
		}

		// ---

		[Fact]
		public void SellGoldEstimationReversedFiat() {

			_ratesProvider.SetSpread(0d);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			var mntAmount = BigInteger.Pow(10, TokensPrecision.EthereumMntp) * 9;
			var fiatAmount = 111111L;
			Assert.True(3333 == CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));
			var rev = CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount)).Result;
			var def = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, rev.ResultGoldAmount).Result;
			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
			Assert.True(def.ResultCentsAmount == fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));

			mntAmount = BigInteger.Pow(10, TokensPrecision.EthereumMntp) * 999;
			fiatAmount = 222222L;
			Assert.True(5555 == CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));
			rev = CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount)).Result;
			def = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, rev.ResultGoldAmount).Result;
			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
			Assert.True(def.ResultCentsAmount == fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));

			mntAmount = BigInteger.Pow(10, TokensPrecision.EthereumMntp) * 9999;
			fiatAmount = 333333L;
			Assert.True(4999 == CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));
			rev = CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount)).Result;
			def = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, rev.ResultGoldAmount).Result;
			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
			Assert.True(def.ResultCentsAmount == fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));

			mntAmount = BigInteger.Pow(10, TokensPrecision.EthereumMntp) * 10000;
			fiatAmount = 444444L;
			Assert.True(3333 == CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));
			rev = CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount)).Result;
			def = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, rev.ResultGoldAmount).Result;
			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
			Assert.True(def.ResultCentsAmount == fiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(fiatAmount, mntAmount));

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
		}

		[Fact]
		public void SellGoldEstimationReversedCrypto() {

			_ratesProvider.SetSpread(0d);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			var requiredCryptoAmount = BigInteger.Pow(10, TokensPrecision.Ethereum);
			Assert.True(requiredCryptoAmount / 1000 == CoreLogic.Finance.Estimation.SellingFeeForCrypto(EthereumToken.Eth, requiredCryptoAmount));

			var rev = CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, requiredCryptoAmount + CoreLogic.Finance.Estimation.SellingFeeForCrypto(EthereumToken.Eth, requiredCryptoAmount)).Result;
			var def = CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, rev.ResultGoldAmount).Result;

			Assert.True(def.Allowed && rev.Allowed);
			Assert.True(def.ResultAssetAmount >= requiredCryptoAmount + CoreLogic.Finance.Estimation.SellingFeeForCrypto(EthereumToken.Eth, requiredCryptoAmount));

			// invalid args
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.InvalidArgs);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, 0).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, 1, -1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
		}

		[Fact]
		public void SellGoldEstimationReversedFiatRandom() {

			for (var i = 0; i < 100000; ++i) {

				_ratesProvider.SetSpread(0.2d);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
				_ratesDispatcher.ForceUpdate().Wait();

				var mntAmount = BigInteger.Pow(10, TokensPrecision.EthereumMntp) * (1 + SecureRandom.GetPositiveInt() % 20000);
				var requiredFiatAmount = (SecureRandom.GetPositiveInt() % 10000) * 100 + (SecureRandom.GetPositiveInt() % 100);

				var rev = CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, requiredFiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(requiredFiatAmount, mntAmount)).Result;
				var def = CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, rev.ResultGoldAmount).Result;

				Assert.True(def.Allowed && rev.Allowed);
				Assert.True(def.ResultCentsAmount == rev.ResultCentsAmount);
				Assert.True(def.ResultCentsAmount == requiredFiatAmount + CoreLogic.Finance.Estimation.SellingFeeForFiat(requiredFiatAmount, mntAmount));
			}
		}

		[Fact]
		public void SellGoldEstimationReversedCryptoRandom() {

			for (var i = 0; i < 100000; ++i) {

				_ratesProvider.SetSpread(0.2d);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
				_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
				_ratesDispatcher.ForceUpdate().Wait();

				var requiredCryptoAmount = (SecureRandom.GetPositiveInt() % 100000000) * BigInteger.Pow(10, TokensPrecision.Ethereum - 8);

				var rev = CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, requiredCryptoAmount + CoreLogic.Finance.Estimation.SellingFeeForCrypto(EthereumToken.Eth, requiredCryptoAmount)).Result;
				var def = CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, rev.ResultGoldAmount).Result;

				Assert.True(def.Allowed && rev.Allowed);
				Assert.True(def.ResultAssetAmount >= requiredCryptoAmount + CoreLogic.Finance.Estimation.SellingFeeForCrypto(EthereumToken.Eth, requiredCryptoAmount));
			}
		}

		// ---

		[Fact]
		public void TradingDisallowed() {

			_ratesProvider.SetSpread(0d);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestGoldRate(TimeSpan.Zero).Result);
			_ratesDispatcher.OnProviderCurrencyRate(_ratesProvider.RequestEthRate(TimeSpan.Zero).Result);
			_ratesDispatcher.ForceUpdate().Wait();

			// ok
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);

			// disallow trading
			RuntimeConfigLoader.EditConfig(cfg => { cfg.Gold.AllowTradingOverall = false; });
			RuntimeConfigHolder.Reload().Wait();

			// disallowed
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			
			// but ok for fiat-ops (with known gold rate)
			var knownGoldRate = 666L;
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.TradingDisallowed);

			// but ok for all ops (with known gold and crypto rate)
			var knownCryptoRate = 777L;
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiat(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate, knownCryptoRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldFiatRev(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.BuyGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate, knownCryptoRate).Result.Status == CoreLogic.Finance.Estimation.BuyGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiat(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCrypto(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate, knownCryptoRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldFiatRev(_services, FiatCurrency.Usd, 1, knownGoldRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
			Assert.True(CoreLogic.Finance.Estimation.SellGoldCryptoRev(_services, EthereumToken.Eth, FiatCurrency.Usd, 1, knownGoldRate, knownCryptoRate).Result.Status == CoreLogic.Finance.Estimation.SellGoldStatus.Success);
		}
	}
}

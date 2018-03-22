﻿using Goldmint.Common;
using Goldmint.CoreLogic.Services.Blockchain;
using Goldmint.DAL;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Numerics;
using System.Threading.Tasks;
using Goldmint.CoreLogic.Finance.Fiat;

namespace Goldmint.QueueService.Workers {

	public class CryptoExchangeRequestHarvester : BaseWorker {

		private readonly BigInteger _blocksPerRound;
		private readonly BigInteger _confirmationsRequired;

		private IServiceProvider _services;
		private ApplicationDbContext _dbContext;
		private IEthereumReader _ethereumReader;

		private BigInteger _lastBlock;
		private BigInteger _lastSavedBlock;
		
		public CryptoExchangeRequestHarvester(int blocksPerRound, int confirmationsRequired) {
			_blocksPerRound = new BigInteger(Math.Max(1, blocksPerRound));
			_confirmationsRequired = new BigInteger(Math.Max(1, confirmationsRequired));
			_lastBlock = BigInteger.Zero;
			_lastSavedBlock = BigInteger.Zero;
		}

		protected override async Task OnInit(IServiceProvider services) {

			_services = services;
			_dbContext = services.GetRequiredService<ApplicationDbContext>();
			_ethereumReader = services.GetRequiredService<IEthereumReader>();
			var appConfig = services.GetRequiredService<AppConfig>();

			// get last block from config
			if (BigInteger.TryParse(appConfig.Services.Ethereum.CryptoExchangeRequest.FromBlock ?? "0", out var lbCfg) && lbCfg >= 0) {
				_lastBlock = lbCfg;
			}

			// get last block from db; remember last saved block
			if (BigInteger.TryParse(await _dbContext.GetDBSetting(DbSetting.LastCryptoExchangeBlockChecked, "0"), out var lbDb) && lbDb >= 0 && lbDb >= lbCfg) {
				_lastBlock = lbDb;
				_lastSavedBlock = lbDb;
			}
		}

		protected override async Task Loop() {

			_dbContext.DetachEverything();

			// get events
			var log = await _ethereumReader.GetEthDepositedEvent(_lastBlock, _lastBlock + _blocksPerRound, _confirmationsRequired);
			_lastBlock = log.ToBlock;
			Logger.Trace($"{log.Events.Length} found in blocks {log.FromBlock} - {log.ToBlock}");

			foreach (var v in log.Events) {

				_dbContext.DetachEverything();

				if (v.RequestId < long.MinValue || v.RequestId > long.MaxValue || !long.TryParse(v.RequestId.ToString(), out var innerRequestId)) {
					Logger.Error($"Cant handle {v.RequestId} in long-value");
					continue;
				}

				await CryptoExchangeQueue.PrepareDepositRequest(
					services: _services,
					asset: CryptoExchangeAsset.ETH,
					internalRequestId: innerRequestId,
					address: v.Address,
					amount: v.EthAmount,
					transactionId: v.TransactionId
				);
			}

			// save last index to settings
			if (_lastSavedBlock != _lastBlock) {
				if (await _dbContext.SaveDbSetting(DbSetting.LastCryptoExchangeBlockChecked, _lastBlock.ToString())) {
					_lastSavedBlock = _lastBlock;
				}
			}
			
		}
	}
}
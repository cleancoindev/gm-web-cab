﻿using Goldmint.Common;
using Goldmint.CoreLogic.Finance.Fiat;
using Goldmint.CoreLogic.Services.Blockchain;
using Goldmint.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Goldmint.QueueService.Workers {

	public class ExchangeRequestHarvester : BaseWorker {

		private IServiceProvider _services;
		private ApplicationDbContext _dbContext;
		private IEthereumReader _ethereumReader;

		private BigInteger _lastIndex;
		private BigInteger _lastSavedIndex;
		
		public ExchangeRequestHarvester() {
			_lastIndex = BigInteger.Zero;
			_lastSavedIndex = BigInteger.Zero;
		}

		protected override async Task OnInit(IServiceProvider services) {
			_services = services;
			_dbContext = services.GetRequiredService<ApplicationDbContext>();
			_ethereumReader = services.GetRequiredService<IEthereumReader>();

			// get from db
			var last = await _dbContext.GetDBSetting(DbSetting.LastExchangeIndex, "0");
			BigInteger.TryParse(last, out _lastIndex);
			_lastSavedIndex = _lastIndex;
		}

		protected override async Task Loop() {

			var currentCount = await _ethereumReader.GetExchangeRequestsCount();

			while (_lastIndex < currentCount) {

				if (IsCancelled()) break;

				var data = await _ethereumReader.GetExchangeRequestByIndex(_lastIndex);

				// is pending
				if (data.IsPending) {

					// TODO: cancel request here in case of: invalid user id, invalid payload, invalid eth address

					if (data.IsBuyRequest) {
						await CoreLogic.Finance.Tokens.GoldToken.PrepareBuyingExchangeRequest(
							services: _services,
							userId: data.UserId,
							payload: data.Payload,
							address: data.Address,
							requestIndex: data.RequestIndex
						);
					} else {
						await CoreLogic.Finance.Tokens.GoldToken.PrepareSellingExchangeRequest(
							services: _services,
							userId: data.UserId,
							payload: data.Payload,
							address: data.Address,
							requestIndex: data.RequestIndex
						);
					}
				}

				_lastIndex++;
			}

			// save last index to settings
			if (_lastSavedIndex != _lastIndex) {
				if (await _dbContext.SaveDbSetting(DbSetting.LastExchangeIndex, _lastIndex.ToString())) {
					_lastSavedIndex = _lastIndex;
				}
			}
		}
	}
}

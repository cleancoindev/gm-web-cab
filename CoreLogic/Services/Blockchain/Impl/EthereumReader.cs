﻿using Goldmint.Common;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Hex.HexTypes;
using Nethereum.Web3;
using NLog;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.RPC.Eth.DTOs;

namespace Goldmint.CoreLogic.Services.Blockchain.Impl {

	public class EthereumReader : EthereumBaseClient, IEthereumReader {

		private const int TransactionMinConfirmationsCount = 2;

		public EthereumReader(AppConfig appConfig, LogFactory logFactory) : base(appConfig, logFactory) {
		}

		public async Task<EthTransactionStatus> CheckTransaction(string transactionId) {

			if (string.IsNullOrWhiteSpace(transactionId)) {
				throw new ArgumentException("Invalid transaction format");
			}

			var web3 = new Web3(JsonRpcClient);
			var txinfo = await web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(transactionId);

			if (txinfo != null) {

				var lastBlockNum = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
				var threshold = BigInteger.One * TransactionMinConfirmationsCount;

				if (
					txinfo.BlockNumber.HexValue != null && // got into block
					lastBlockNum.Value - txinfo.BlockNumber.Value >= threshold // wait for number of confirmation
				) {
					// check status
					if ((txinfo.Status?.Value ?? BigInteger.Zero) == BigInteger.One) {
						return EthTransactionStatus.Success;
					}
					return EthTransactionStatus.Failed;
				}
				return EthTransactionStatus.Pending;
			}

			// assume it is pending
			return EthTransactionStatus.Pending;
		}

		public async Task<long> GetUserFiatBalance(string userId, FiatCurrency currency) {

			if (string.IsNullOrWhiteSpace(userId)) {
				throw new ArgumentException("Invalid user ID");
			}

			if (currency == FiatCurrency.USD) {
				var web3 = new Web3(JsonRpcClient);
				var contract = web3.Eth.GetContract(
					FiatContractABI,
					FiatContractAddress
				);

				var func = contract.GetFunction("getUserFiatBalance");
				var funcRet = await func.CallAsync<BigInteger>(userId);

				return funcRet > long.MaxValue? long.MaxValue: (long)funcRet;
			}

			throw new NotImplementedException("Currency not implemented");
		}

		public async Task<BigInteger> GetUserGoldBalance(string userId) {

			if (string.IsNullOrWhiteSpace(userId)) {
				throw new ArgumentException("Invalid user ID");
			}

			var web3 = new Web3(JsonRpcClient);
			var contract = web3.Eth.GetContract(
				FiatContractABI,
				FiatContractAddress
			);

			var func = contract.GetFunction("getUserHotGoldBalance");
			var funcRet = await func.CallAsync<BigInteger>(userId);

			return funcRet;
		}

		public async Task<BigInteger> GetAddressGoldBalance(string address) {

			if (string.IsNullOrWhiteSpace(address)) {
				throw new ArgumentException("Invalid address format");
			}

			var web3 = new Web3(JsonRpcClient);

			var contract = web3.Eth.GetContract(
				"[{\"constant\":true,\"inputs\":[{\"name\":\"_owner\",\"type\":\"address\"}],\"name\":\"balanceOf\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"}]",
				GoldTokenContractAddress
			);
			var func = contract.GetFunction("balanceOf");
			var funcRet = await func.CallAsync<BigInteger>(address);

			return funcRet;
		}

		public async Task<BigInteger> GetAddressMntpBalance(string address) {

			if (string.IsNullOrWhiteSpace(address)) {
				throw new ArgumentException("Invalid address format");
			}

			var web3 = new Web3(JsonRpcClient);

			var contract = web3.Eth.GetContract(
				"[{\"constant\":true,\"inputs\":[{\"name\":\"_owner\",\"type\":\"address\"}],\"name\":\"balanceOf\",\"outputs\":[{\"name\":\"\",\"type\":\"uint256\"}],\"payable\":false,\"stateMutability\":\"view\",\"type\":\"function\"}]",
				MntpTokenContractAddress
			);
			var func = contract.GetFunction("balanceOf");
			var funcRet = await func.CallAsync<BigInteger>(address);

			return funcRet;
		}

		public async Task<BigInteger> GetExchangeRequestsCount() {

			var web3 = new Web3(JsonRpcClient);

			var contract = web3.Eth.GetContract(
					FiatContractABI,
					FiatContractAddress
				);
			var func = contract.GetFunction("getRequestsCount");
			return await func.CallAsync<BigInteger>();
		}

		public async Task<ExchangeRequestData> GetExchangeRequestByIndex(BigInteger requestIndex) {

			var web3 = new Web3(JsonRpcClient);

			var contract = web3.Eth.GetContract(
				FiatContractABI,
				FiatContractAddress
			);
			var func = contract.GetFunction("getRequest");
			var funcRet = await func.CallDeserializingToObjectAsync<ExchangeRequestByIndexResult>(requestIndex);

			return new ExchangeRequestData() {
				RequestIndex = requestIndex,
				Payload = funcRet.Payload,
				Address = funcRet.Address,
				UserId = funcRet.UserId,
				IsBuyRequest = funcRet.IsBuyRequest,
				IsPending = funcRet.State == 0,
				IsSucceeded = funcRet.State == 1,
				IsCancelled = funcRet.State == 2,
			};
		}

		public async Task<EthDepositedResult> GetEthDepositedEvent(BigInteger from, BigInteger to, BigInteger confirmationsRequired) {

			var web3 = new Web3(JsonRpcLogsClient);

			var contract = web3.Eth.GetContract(
				FiatContractABI,
				FiatContractAddress
			);

			var hexLaxtestBlock = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
			var latestConfirmedBlock = hexLaxtestBlock.Value -= confirmationsRequired;

			var hexFromBlock = new HexBigInteger(BigInteger.Min(from, latestConfirmedBlock));
			var hexToBlock = new HexBigInteger(BigInteger.Min(to, latestConfirmedBlock));

			var evnt = contract.GetEvent("EthDeposited");
			var filter = await evnt.CreateFilterBlockRangeAsync(
				new BlockParameter(hexFromBlock),
				new BlockParameter(hexToBlock)
			);

			var events = new List<EthDepositedEventData>();
			var logs = await evnt.GetAllChanges<EthereumDepositEventResult>(filter);

			foreach (var v in logs) {
				events.Add(new EthDepositedEventData() {
					Address = v.Event.Address,
					EthAmount = v.Event.EthValue,
					RequestId = v.Event.RequestId,
					BlockNumber = v.Log.BlockNumber,
					TransactionId = v.Log.TransactionHash,
				});
			}

			return new EthDepositedResult() {
				FromBlock = hexFromBlock.Value,
				ToBlock = hexToBlock.Value,
				Events = events.ToArray(),
			};
		}

		// ---

		[FunctionOutput]
		private class ExchangeRequestByIndexResult {

			[Parameter("address", 1)]
			public string Address { get; set; }

			[Parameter("string", 2)]
			public string UserId { get; set; }

			[Parameter("string", 3)]
			public string Payload { get; set; }

			[Parameter("bool", 4)]
			public bool IsBuyRequest { get; set; }

			[Parameter("uint8", 5)]
			public int State { get; set; }
		}

		private class EthereumDepositEventResult {

			[Parameter("uint", "_requestId", 1, true)]
			public BigInteger RequestId { get; set; }

			[Parameter("address", "_address", 2, true)]
			public string Address { get; set; }

			[Parameter("uint", "_ethValue", 3, false)]
			public BigInteger EthValue { get; set; }
		}
	}
}
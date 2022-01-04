using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using WalletWasabi.Helpers;
using WalletWasabi.Tests.Helpers;
using WalletWasabi.WabiSabi;
using WalletWasabi.WabiSabi.Client;
using Xunit;

namespace WalletWasabi.Tests.UnitTests.WabiSabi.Client
{
	public class AmountDecomposerTests
	{
		private static readonly Random Random = new (1234567);

		[Theory]
		[InlineData(0, 0, 8)]
		[InlineData(0, 0, 1)]
		[InlineData(0, 0, 2)]
		[InlineData(0, 0, 3)]
		[InlineData(0, 1_000, 1)]
		[InlineData(0, 100_000, 2)]
		[InlineData(0, 1_000_000, 3)]
		[InlineData(0, 10_000_000, 8)]
		[InlineData(20, 0, 1)]
		[InlineData(100, 0, 2)]
		[InlineData(500, 0, 3)]
		[InlineData(5000, 0, 8)]
		public void DecompositionsInvariantTest(decimal feeRateDecimal, long minOutputAmount, int maxAvailableOutputs)
		{
			var availableVsize = maxAvailableOutputs * Constants.P2WPKHOutputSizeInBytes;
			var feeRate = new FeeRate(feeRateDecimal);
			var feePerOutput = feeRate.GetFee(Constants.P2WPKHOutputSizeInBytes);
			var registeredCoins = GenerateRandomCoins().Take(3).ToList();
			var theirCoins = GenerateRandomCoins().Take(30).ToList();

			var amountDecomposer = new AmountDecomposer(feeRate, minOutputAmount, Constants.P2WPKHOutputSizeInBytes, availableVsize);
			var outputValues = amountDecomposer.Decompose(registeredCoins, theirCoins);

			var totalEffectiveValue = registeredCoins.Sum(x => x.EffectiveValue(feeRate));
			var totalEffectiveCost = outputValues.Count() * feePerOutput;

			Assert.InRange(outputValues.Count(), 1, maxAvailableOutputs);
			Assert.True(totalEffectiveValue - totalEffectiveCost - minOutputAmount <= outputValues.Sum());
			Assert.All(outputValues, v => Assert.InRange(v.Satoshi, minOutputAmount, totalEffectiveValue));
		}

		private static IEnumerable<Coin> GenerateRandomCoins()
		{
			using var key = new Key();
			var script = key.GetScriptPubKey(ScriptPubKeyType.Segwit);
			while (true)
			{
				var amount = Random.NextInt64(100_000, ProtocolConstants.MaxAmountPerAlice);
				yield return CreateCoin(script, amount);
			}
		}

		private static Coin CreateCoin(Script scriptPubKey, long amount)
		{
			var prevOut = BitcoinFactory.CreateOutPoint();
			var txOut = new TxOut(Money.Satoshis(amount), scriptPubKey);
			return new Coin(prevOut, txOut);
		}
	}
}

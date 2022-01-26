using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.Analysis.Clustering;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.Wallets.Send;

public partial class PrivacySuggestionsFlyoutViewModel : ViewModelBase
{
	[AutoNotify] private SuggestionViewModel? _previewSuggestion;
	[AutoNotify] private SuggestionViewModel? _selectedSuggestion;
	[AutoNotify] private bool _isOpen;
	[AutoNotify] private bool _isLoading;

	public PrivacySuggestionsFlyoutViewModel()
	{
		Suggestions = new ObservableCollection<SuggestionViewModel>();

		this.WhenAnyValue(x => x.IsOpen)
			.Subscribe(x =>
			{
				if (!x)
				{
					PreviewSuggestion = null;
				}
			});
	}

	public ObservableCollection<SuggestionViewModel> Suggestions { get; }

	public async Task BuildPrivacySuggestionsAsync(Wallet wallet, TransactionInfo info, BitcoinAddress destination, BuildTransactionResult transaction, CancellationToken cancellationToken = default)
	{
		IsLoading = true;

		Suggestions.Clear();
		SelectedSuggestion = null;

		if (!info.IsPrivate)
		{
			Suggestions.Add(new PocketSuggestionViewModel(SmartLabel.Merge(transaction.SpentCoins.Select(x => CoinHelpers.GetLabels(x)))));
		}

		var loadingRing = new LoadingSuggestionViewModel();
		Suggestions.Add(loadingRing);

		var hasChange = transaction.InnerWalletOutputs.Any(x => x.ScriptPubKey != destination.ScriptPubKey);

		IsLoading = false;

		if (hasChange)
		{
			var suggestions =
				ChangeAvoidanceSuggestionViewModel.GenerateSuggestionsAsync(info, destination, wallet, transaction, cancellationToken);

			await foreach (var suggestion in suggestions)
			{
				Suggestions.Insert(Suggestions.Count - 1, suggestion);
			}

			Suggestions.Remove(loadingRing);
		}
	}
}

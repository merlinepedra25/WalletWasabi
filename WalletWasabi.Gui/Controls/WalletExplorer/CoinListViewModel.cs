﻿using Avalonia.Controls;
using System.Collections.Generic;
using NBitcoin;
using ReactiveUI;
using ReactiveUI.Legacy;
using System;
using System.Linq;
using WalletWasabi.Gui.Models;
using WalletWasabi.Gui.ViewModels;
using System.ComponentModel;
using System.Collections.Specialized;
using WalletWasabi.Models;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Threading;
using DynamicData;
using DynamicData.Binding;

namespace WalletWasabi.Gui.Controls.WalletExplorer
{
	public class CoinListViewModel : ViewModelBase
	{

		public ReadOnlyObservableCollection<CoinViewModel> Coins => _coinViewModels;
		private readonly ReadOnlyObservableCollection<CoinViewModel> _coinViewModels;

		SourceList<SmartCoin> _rootlist = new SourceList<SmartCoin>();
		SortExpressionComparer<CoinViewModel> _myComparer;
		SortExpressionComparer<CoinViewModel> MyComparer
		{
			get => _myComparer;
			set
			{
				this.RaiseAndSetIfChanged(ref _myComparer, value);
			}
		}

#pragma warning disable CS0618 // Type or member is obsolete

		private CoinViewModel _selectedCoin;
		private bool? _selectAllCheckBoxState;
		private SortOrder _statusSortDirection;
		private SortOrder _privacySortDirection;
		private SortOrder _amountSortDirection;
		private bool? _selectPrivateCheckBoxState;
		private bool? _selectNonPrivateCheckBoxState;
		private GridLength _coinJoinStatusWidth;
		private SortOrder _historySortDirection;
		private SortExpressionComparer<CoinViewModel> _myComparer1;

		public ReactiveCommand EnqueueCoin { get; }
		public ReactiveCommand DequeueCoin { get; }
		public ReactiveCommand SelectAllCheckBoxCommand { get; }
		public ReactiveCommand SelectPrivateCheckBoxCommand { get; }
		public ReactiveCommand SelectNonPrivateCheckBoxCommand { get; }

		public event Action DequeueCoinsPressed;

		public CoinViewModel SelectedCoin
		{
			get => _selectedCoin;
			set
			{
				this.RaiseAndSetIfChanged(ref _selectedCoin, value);
				this.RaisePropertyChanged(nameof(CanDeqeue));
			}
		}

		public bool CanDeqeue => SelectedCoin is null ? false : SelectedCoin.CoinJoinInProgress;

		public bool? SelectAllCheckBoxState
		{
			get => _selectAllCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectAllCheckBoxState, value);
		}

		public bool? SelectPrivateCheckBoxState
		{
			get => _selectPrivateCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectPrivateCheckBoxState, value);
		}

		public SortOrder StatusSortDirection
		{
			get => _statusSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _statusSortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					PrivacySortDirection = SortOrder.None;
					HistorySortDirection = SortOrder.None;
				}
			}
		}


		public SortOrder AmountSortDirection
		{
			get => _amountSortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _amountSortDirection, value);
				if (value != SortOrder.None)
				{
					PrivacySortDirection = SortOrder.None;
					StatusSortDirection = SortOrder.None;
					HistorySortDirection = SortOrder.None;
				}
				RefreshOrdering();
			}
		}

		public SortOrder PrivacySortDirection
		{
			get => _privacySortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _privacySortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					StatusSortDirection = SortOrder.None;
					HistorySortDirection = SortOrder.None;
				}
			}
		}

		public SortOrder HistorySortDirection
		{
			get => _historySortDirection;
			set
			{
				this.RaiseAndSetIfChanged(ref _historySortDirection, value);
				if (value != SortOrder.None)
				{
					AmountSortDirection = SortOrder.None;
					StatusSortDirection = SortOrder.None;
					PrivacySortDirection = SortOrder.None;
				}
			}
		}

		void RefreshOrdering()
		{
			if (AmountSortDirection != SortOrder.None)
			{
				if (AmountSortDirection == SortOrder.Increasing)
					MyComparer = SortExpressionComparer<CoinViewModel>.Ascending(cmv => cmv.Amount);
				else
					MyComparer = SortExpressionComparer<CoinViewModel>.Descending(cmv => cmv.Amount);
			}
		}

		public bool? SelectNonPrivateCheckBoxState
		{
			get => _selectNonPrivateCheckBoxState;
			set => this.RaiseAndSetIfChanged(ref _selectNonPrivateCheckBoxState, value);
		}

		public GridLength CoinJoinStatusWidth
		{
			get => _coinJoinStatusWidth;
			set => this.RaiseAndSetIfChanged(ref _coinJoinStatusWidth, value);
		}

		private bool? GetCheckBoxesSelectedState(Func<CoinViewModel, bool> coinFilterPredicate)
		{
			var coins = Coins.Where(coinFilterPredicate).ToArray();
			bool IsAllSelected = true;
			foreach (CoinViewModel coin in coins)
				if (!coin.IsSelected)
				{
					IsAllSelected = false;
					break;
				}
			bool IsAllDeselected = true;
			foreach (CoinViewModel coin in coins)
				if (coin.IsSelected)
				{
					IsAllDeselected = false;
					break;
				}
			if (IsAllDeselected) return false;
			if (IsAllSelected) return true;
			return null;
		}

		private void SelectAllCoins(bool valueOfSelected, Func<CoinViewModel, bool> coinFilterPredicate)
		{
			var coins = Coins.Where(coinFilterPredicate).ToArray();
			foreach (var c in coins)
			{
				c.IsSelected = valueOfSelected;
			}
		}

		public CoinListViewModel(Money preSelectMinAmountIncludingCondition = null, int? preSelectMaxAnonSetExcludingCondition = null)
		{
			MyComparer = SortExpressionComparer<CoinViewModel>.Ascending(cmv => cmv.Amount);

			//IObservable<IComparer<CoinViewModel>> comparerObservable = MyComparer.Subscribe();

			_rootlist.AddRange(Global.WalletService.Coins);

			_rootlist.Connect()
				.Transform(sc => new CoinViewModel(sc))
				.OnItemAdded(cvm => cvm.PropertyChanged += Coin_PropertyChanged)
				.OnItemRemoved(cvm => cvm.PropertyChanged -= Coin_PropertyChanged)
				.Filter(cvm => cvm.Unspent)
				.Sort(MyComparer)
				.Bind(out _coinViewModels)
				.Subscribe();

			Global.WalletService.Coins.CollectionChanged += Coins_CollectionGlobalChanged;

			if (preSelectMinAmountIncludingCondition != null && preSelectMaxAnonSetExcludingCondition != null)
			{
				foreach (CoinViewModel coin in Coins)
				{
					if (coin.Amount >= preSelectMinAmountIncludingCondition && coin.AnonymitySet < preSelectMaxAnonSetExcludingCondition)
					{
						coin.IsSelected = true;
					}
				}
			}

			EnqueueCoin = ReactiveCommand.Create(() =>
			{
				if (SelectedCoin == null) return;
				//await Global.ChaumianClient.QueueCoinsToMixAsync()
			});

			DequeueCoin = ReactiveCommand.Create(() =>
			{
				if (SelectedCoin == null) return;
				DequeueCoinsPressed?.Invoke();
			}, this.WhenAnyValue(x => x.CanDeqeue));

			SelectAllCheckBoxCommand = ReactiveCommand.Create(() =>
			{
				Global.WalletService.Coins.TryRemove(Coins[0].Model);
				switch (SelectAllCheckBoxState)
				{
					case true:
						SelectAllCoins(true, x => true);
						break;

					case false:
						SelectAllCoins(false, x => true);
						break;

					case null:
						SelectAllCoins(false, x => true);
						SelectAllCheckBoxState = false;
						break;
				}
			});

			SelectPrivateCheckBoxCommand = ReactiveCommand.Create(() =>
			{
				switch (SelectPrivateCheckBoxState)
				{
					case true:
						SelectAllCoins(true, x => x.AnonymitySet >= 50);
						break;

					case false:
						SelectAllCoins(false, x => x.AnonymitySet >= 50);
						break;

					case null:
						SelectAllCoins(false, x => x.AnonymitySet >= 50);
						SelectPrivateCheckBoxState = false;
						break;
				}
			});

			SelectNonPrivateCheckBoxCommand = ReactiveCommand.Create(() =>
			{
				switch (SelectNonPrivateCheckBoxState)
				{
					case true:
						SelectAllCoins(true, x => x.AnonymitySet < 50);
						break;

					case false:
						SelectAllCoins(false, x => x.AnonymitySet < 50);
						break;

					case null:
						SelectAllCoins(false, x => x.AnonymitySet < 50);
						SelectNonPrivateCheckBoxState = false;
						break;
				}
			});
			SetSelections();
			SetCoinJoinStatusWidth();
			//AmountSortDirection = SortOrder.Decreasing;
		}


		void Coins_CollectionGlobalChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			Dispatcher.UIThread.Post(() =>
			{
				switch (e.Action)
				{
					case NotifyCollectionChangedAction.Add:
						foreach (var c in e.NewItems.Cast<SmartCoin>())
							_rootlist.Add(c);
						break;
					case NotifyCollectionChangedAction.Remove:
						foreach (var c in e.OldItems.Cast<SmartCoin>())
							_rootlist.Remove(c);
						break;
					case NotifyCollectionChangedAction.Reset:
						_rootlist.Clear();
						break;
				}
			});
		}


		private void SetSelections()
		{
			SelectAllCheckBoxState = GetCheckBoxesSelectedState(x => true);
			SelectPrivateCheckBoxState = GetCheckBoxesSelectedState(x => x.AnonymitySet >= 50);
			SelectNonPrivateCheckBoxState = GetCheckBoxesSelectedState(x => x.AnonymitySet < 50);
		}

		private void SetCoinJoinStatusWidth()
		{
			if (Coins.Any(x => x.Status == SmartCoinStatus.MixingConnectionConfirmation
				 || x.Status == SmartCoinStatus.MixingInputRegistration
				 || x.Status == SmartCoinStatus.MixingOnWaitingList
				 || x.Status == SmartCoinStatus.MixingOutputRegistration
				 || x.Status == SmartCoinStatus.MixingSigning
				 || x.Status == SmartCoinStatus.MixingWaitingForConfirmation
				 || x.Status == SmartCoinStatus.SpentAccordingToBackend))
			{
				CoinJoinStatusWidth = new GridLength(180);
			}
			else
			{
				CoinJoinStatusWidth = new GridLength(0);
			}
		}

		private void Coin_PropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (e.PropertyName == nameof(CoinViewModel.IsSelected))
			{
				SetSelections();
			}
			if (e.PropertyName == nameof(CoinViewModel.Status))
			{
				SetCoinJoinStatusWidth();
			}
		}

#pragma warning restore CS0618 // Type or member is obsolete
	}
}

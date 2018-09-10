﻿namespace GWallet.Frontend.XF

open System
open System.Linq
open System.Threading.Tasks

open Xamarin.Forms
open Xamarin.Forms.Xaml
open ZXing
open ZXing.Mobile
open ZXing.Net.Mobile.Forms

open GWallet.Backend

type PairingToPage(balancesPage: Page,
                   normalAccountsAndBalances: seq<IAccount*Label*Label*MaybeCached<decimal>*bool>,
                   newBalancesPageFunc: seq<IAccount*Label*Label*_*_>*seq<IAccount*Label*Label*_*_> -> Page) =
    inherit ContentPage()
    let _ = base.LoadFromXaml(typeof<PairingToPage>)

    let mainLayout = base.FindByName<StackLayout>("mainLayout")
    let scanQrCodeButton = mainLayout.FindByName<Button>("scanQrCode")
    let coldAddressesEntry = mainLayout.FindByName<Entry>("coldStorageAddresses")
    let pairButton = mainLayout.FindByName<Button>("pairButton")
    let cancelButton = mainLayout.FindByName<Button>("cancelButton")
    do
        if Device.RuntimePlatform = Device.Android || Device.RuntimePlatform = Device.iOS then
            scanQrCodeButton.IsVisible <- true

    let GuessCurrenciesOfAddress (address: string): List<Currency> =
        try
            Account.ValidateUnknownCurrencyAddress address
        with
        | AddressMissingProperPrefix _ ->
            List.empty
        | AddressWithInvalidLength _ ->
            List.empty
        | AddressWithInvalidChecksum _ ->
            List.empty

    member this.OnScanQrCodeButtonClicked(sender: Object, args: EventArgs): unit =
        let scanPage = ZXingScannerPage FrontendHelpers.BarCodeScanningOptions
        scanPage.add_OnScanResult(fun result ->
            scanPage.IsScanning <- false

            Device.BeginInvokeOnMainThread(fun _ ->
                let task = this.Navigation.PopModalAsync()
                task.ContinueWith(fun (t: Task<Page>) ->
                    Device.BeginInvokeOnMainThread(fun _ ->
                        coldAddressesEntry.Text <- result.Text
                    )
                ) |> FrontendHelpers.DoubleCheckCompletionNonGeneric
            )
        )
        Device.BeginInvokeOnMainThread(fun _ ->
            this.Navigation.PushModalAsync scanPage
                |> FrontendHelpers.DoubleCheckCompletionNonGeneric
        )

    member this.OnEntryTextChanged(sender: Object, args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            pairButton.IsEnabled <- not (String.IsNullOrEmpty coldAddressesEntry.Text)
        )

    member this.OnCancelButtonClicked(sender: Object, args: EventArgs) =
        Device.BeginInvokeOnMainThread(fun _ ->
            balancesPage.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
        )

    member this.OnPairButtonClicked(sender: Object, args: EventArgs): unit =
        let addresses = coldAddressesEntry.Text.Split(',')

        //FIXME: we should give specific info (to the user) about why an address is not valid
        let addressesToCurrencies = Seq.map (fun addr -> addr,(GuessCurrenciesOfAddress addr)) addresses
        if addressesToCurrencies.Any(fun (_,currencies) -> currencies = List.empty) then
            let msg = "Some address doesn't seem to be valid, please try again."
            this.DisplayAlert("Alert", msg, "OK") |> ignore
        elif (normalAccountsAndBalances.Any(fun (account,_,_,_,_) ->
                                                 addresses.Any(fun addr -> account.PublicAddress = addr))) then
            let msg = "Some address matches to an account that is already being held in this wallet."
            this.DisplayAlert("Alert", msg, "OK") |> ignore
        else
            Device.BeginInvokeOnMainThread(fun _ ->
                pairButton.IsEnabled <- false
                pairButton.Text <- "Pairing..."
                coldAddressesEntry.IsEnabled <- false
                cancelButton.IsEnabled <- false
                scanQrCodeButton.IsEnabled <- false
            )
            for addr,currencies in addressesToCurrencies do
                for currency in currencies do
                    Account.AddPublicWatcher currency addr

            let readOnlyAccounts = Account.GetAllActiveAccounts().OfType<ReadOnlyAccount>() |> List.ofSeq
                                   |> List.map (fun account -> account :> IAccount)
            let readOnlyAccountsWithLabels = FrontendHelpers.CreateWidgetsForAccounts readOnlyAccounts
            let checkReadOnlyBalancesInParallel =
                seq {
                    for account,l1,l2 in readOnlyAccountsWithLabels do
                        yield FrontendHelpers.UpdateBalanceAsync account l1 l2
                } |> Async.Parallel
            let normalAccountsBalancesJob =
                FrontendHelpers.UpdateCachedBalancesAsync (normalAccountsAndBalances.Select(fun (a,l1,l2,_,_) -> a,l1,l2))

            let updateBalancesInParallelAndSwitchBackToBalPage = async {
                let allBalancesJob = Async.Parallel(normalAccountsBalancesJob::(checkReadOnlyBalancesInParallel::[]))
                let! allResolvedBalances = allBalancesJob
                let allResolvedNormalAccountBalances = allResolvedBalances.ElementAt(0)
                let allResolvedReadOnlyBalances = allResolvedBalances.ElementAt(1)

                Device.BeginInvokeOnMainThread(fun _ ->
                    let newBalancesPage = newBalancesPageFunc(allResolvedNormalAccountBalances,
                                                              allResolvedReadOnlyBalances)
                    let navNewBalancesPage = NavigationPage(newBalancesPage)
                    NavigationPage.SetHasNavigationBar(newBalancesPage, false)
                    NavigationPage.SetHasNavigationBar(navNewBalancesPage, false)

                    // FIXME: BalancePage should probably be IDisposable and remove timers when disposing
                    balancesPage.Navigation.RemovePage balancesPage

                    this.Navigation.InsertPageBefore(navNewBalancesPage, this)

                    this.Navigation.PopAsync() |> FrontendHelpers.DoubleCheckCompletion
                )
            }
            Async.StartAsTask updateBalancesInParallelAndSwitchBackToBalPage
                |> FrontendHelpers.DoubleCheckCompletion


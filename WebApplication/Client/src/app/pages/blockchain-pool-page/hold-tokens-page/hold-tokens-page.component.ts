import {ChangeDetectionStrategy, ChangeDetectorRef, Component, HostBinding, OnDestroy, OnInit} from '@angular/core';
import {BigNumber} from "bignumber.js";
import {CommonService} from "../../../services/common.service";
import {EthereumService, MessageBoxService, UserService} from "../../../services";
import {Subject} from "rxjs/Subject";
import {Subscription} from "rxjs/Subscription";
import {environment} from "../../../../environments/environment";
import {PoolService} from "../../../services/pool.service";
import {TranslateService} from "@ngx-translate/core";
import * as Web3 from "web3";
import {Router} from "@angular/router";

@Component({
  selector: 'app-hold-tokens-page',
  templateUrl: './hold-tokens-page.component.html',
  styleUrls: ['./hold-tokens-page.component.sass'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class HoldTokensPageComponent implements OnInit, OnDestroy {

  @HostBinding('class') class = 'page';

  public loading: boolean = false;
  public tokenBalance: BigNumber | null = null;
  public ethAddress: string = '';
  public tokenAmount: number = 0;
  public etherscanUrl = environment.etherscanUrl;
  public interval: Subscription;
  public MMNetwork = environment.MMNetwork;

  public invalidBalance: boolean = false;
  public isInvalidNetwork: boolean = true;
  public noMetamask: boolean = false;
  public allowance: number = null;
  public resetTokenAllowance: boolean = false;

  private Web3 = new Web3();
  private timeoutPopUp;
  private destroy$: Subject<boolean> = new Subject<boolean>();
  private sub1: Subscription;

  constructor(
    private _commonService: CommonService,
    private _userService: UserService,
    private _cdRef: ChangeDetectorRef,
    private _ethService: EthereumService,
    private _messageBox: MessageBoxService,
    private _poolService: PoolService,
    private _translate: TranslateService,
    private _router: Router
  ) { }

  ngOnInit() {
    this.initSuccessTransactionModal();

    if (!window['ethereum'] || !window['ethereum'].isMetaMask) {
      this.noMetamask = true;
      this._cdRef.markForCheck();
    }

    this._ethService.getObservableMntpBalance().takeUntil(this.destroy$).subscribe(balance => {
      if (balance !== null && (this.tokenBalance === null || !this.tokenBalance.eq(balance))) {
        this.tokenBalance = +balance < 1 * Math.pow(10, -6) ? new BigNumber(0) : balance;
        this.setCoinBalance(1);
        this.loading = false;
        this._cdRef.markForCheck();
      }
    });

    this._ethService.getObservableEthAddress().takeUntil(this.destroy$).subscribe(ethAddr => {
      if (!this.ethAddress && ethAddr) {
        this._messageBox.closeModal();
      }
      this.ethAddress = ethAddr;
      if (ethAddr) {
        this._ethService.getPoolTokenAllowance(ethAddr).subscribe((res: any) => {
          this.allowance = res;
          this.tokenAmount && this.checkAllowanceState(this.tokenAmount);
        });
      }
      if (!this.ethAddress && this.tokenBalance !== null) {
        this.tokenBalance = null;
        this.tokenAmount = 0;
      }
      this._cdRef.markForCheck();
    });

    this._ethService.getObservableNetwork().takeUntil(this.destroy$).subscribe(network => {
      if (network !== null) {
        if (network != this.MMNetwork.index) {
          this._userService.invalidNetworkModal(this.MMNetwork.name);
          this.isInvalidNetwork = true;
        } else {
          this.isInvalidNetwork = false;
        }
        this._cdRef.markForCheck();
      }
    });

  }

  initSuccessTransactionModal() {
    this._poolService.getSuccessHoldRequestLink$.takeUntil(this.destroy$).subscribe(hash => {
      if (hash) {
        this._translate.get('MessageBox.SuccessTransactionModal').subscribe(phrases => {
          setTimeout(() => {
            this._poolService.successTransactionModal(hash, phrases);
          }, 600);
          this._router.navigate(['/ethereum-pool']);
        });
      }
    });
  }

  getMetamaskModal() {
    this._userService.showGetMetamaskModal();
  }

  enableMetamaskModal() {
    this._ethService.connectToMetaMask();
    this._userService.showLoginToMMBox('HeadingPool');
  }

  changeValue(event) {
    event.target.value = this._commonService.substrValue(event.target.value);
    this.tokenAmount = +event.target.value;
    event.target.setSelectionRange(event.target.value.length, event.target.value.length);
    this.checkEnteredAmount();
    this._cdRef.markForCheck();
  }

  setCoinBalance(percent) {
    const value = this._commonService.substrValue(+this.tokenBalance * percent);
    this.tokenAmount = +value;
    this.checkEnteredAmount();
    this._cdRef.markForCheck();
  }

  checkEnteredAmount() {
    this.invalidBalance = this.tokenAmount > +this.tokenBalance;
    this.checkAllowanceState(this.tokenAmount);
  }

  checkAllowanceState(amount: number) {
    this.resetTokenAllowance = this.allowance !== 0 && this.allowance !== amount;
    this._cdRef.markForCheck();
  }

  onSubmit() {
    let firstLoad = true;
    this.sub1 && this.sub1.unsubscribe();
    this.sub1 = this._ethService.getObservableGasPrice().takeUntil(this.destroy$).subscribe((price) => {
      if (price && firstLoad) {
        firstLoad = false;
        this._poolService.holdStake(this.ethAddress, this.tokenAmount, +price * Math.pow(10, 9));
      }
    });
  }

  ngOnDestroy() {
    this.destroy$.next(true);
    clearTimeout(this.timeoutPopUp);
  }

}

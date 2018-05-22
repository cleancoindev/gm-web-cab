import {ChangeDetectorRef, Component, HostBinding, OnInit, ViewChild} from '@angular/core';
import {Subject} from "rxjs/Subject";
import {APIService, EthereumService, GoldrateService, MessageBoxService, UserService} from "../../../services";
import {TranslateService} from "@ngx-translate/core";
import {BigNumber} from "bignumber.js";
import * as Web3 from "web3";
import {Router} from "@angular/router";

@Component({
  selector: 'app-buy-card-page',
  templateUrl: './buy-card-page.component.html',
  styleUrls: ['./buy-card-page.component.sass']
})
export class BuyCardPageComponent implements OnInit {
  @HostBinding('class') class = 'page';

  @ViewChild('goldAmountInput') goldAmountInput;
  @ViewChild('usdAmountInput') usdAmountInput;

  public loading = false;
  public locale: string;
  public invalidBalance = false;

  public isReversed: boolean = false;
  public goldAmount: number = 0;
  public usdAmount: number = 0;
  public ethAddress: string = '';
  public currentValue: number;
  public estimatedAmount: BigNumber;

  private Web3 = new Web3();
  private destroy$: Subject<boolean> = new Subject<boolean>();

  constructor(
    private _userService: UserService,
    private _apiService: APIService,
    private _messageBox: MessageBoxService,
    private _ethService: EthereumService,
    private _goldrateService: GoldrateService,
    private _cdRef: ChangeDetectorRef,
    private _translate: TranslateService,
    private router: Router
  ) { }

  ngOnInit() {
    this.goldAmountInput.valueChanges
      .debounceTime(500)
      .distinctUntilChanged()
      .takeUntil(this.destroy$)
      .subscribe(value => {
        if (value && !this.isReversed) {
          this.onAmountChanged(this.currentValue);
          this._cdRef.markForCheck();
        }
      });

    this.usdAmountInput.valueChanges
      .debounceTime(500)
      .distinctUntilChanged()
      .takeUntil(this.destroy$)
      .subscribe(value => {
        if (value && this.isReversed) {
          this.onAmountChanged(this.currentValue);
          this._cdRef.markForCheck();
        }
      });

    this._userService.currentLocale.takeUntil(this.destroy$).subscribe(currentLocale => {
      this.locale = currentLocale;
    });

    this._ethService.getObservableEthAddress().takeUntil(this.destroy$).subscribe(ethAddr => {
      ethAddr !== null && (this.ethAddress = ethAddr);
      this.ethAddress && ethAddr === null && this.router.navigate(['sell']);
    });

  }

  onAmountChanged(value: number) {
    this.loading = true;

    if (!this.isReversed) {
      if (value > 1 && value.toString().length <= 15) {

        this.estimatedAmount = new BigNumber(value).decimalPlaces(6, BigNumber.ROUND_DOWN);
        const usd = (value * 100).toFixed();

        this._apiService.goldBuyEstimate('USD', usd, false)
          .finally(() => {
            this.loading = false;
            this._cdRef.markForCheck();
          }).subscribe(data => {
          this.goldAmount = +this.substrValue(data.data.amount / Math.pow(10, 18));
          this.invalidBalance = false;
        });
      } else {
        this.goldAmount = 0;
        this.setError();
      }
    }
    if (this.isReversed) {
      if (value > 0 && value.toString().length <= 15) {

        const wei = this.Web3.toWei(value);
        this.estimatedAmount = new BigNumber(value).decimalPlaces(6, BigNumber.ROUND_DOWN);

        this._apiService.goldBuyEstimate('USD', wei, true)
          .finally(() => {
            this.loading = false;
            this._cdRef.markForCheck();
          }).subscribe(data => {
          this.usdAmount = data.data.amount;
          this.invalidBalance = (this.usdAmount <= 1) ? true : false;
        });
      } else {
        this.setError();
      }
    }
  }

  setError() {
    this.invalidBalance = true;
    this.loading = false;
    this._cdRef.markForCheck();
  }

  changeValue(status: boolean, event) {
    event.target.value = this.substrValue(event.target.value);
    this.currentValue = +event.target.value;
    event.target.setSelectionRange(event.target.value.length, event.target.value.length);

    if (status !== this.isReversed) {
      this.isReversed = status;
      this.invalidBalance = false;
      this.loading = true;
    }
    this._cdRef.markForCheck();
  }

  substrValue(value: number|string) {
    return value.toString()
      .replace(',', '.')
      .replace(/([^\d.])|(^\.)/g, '')
      .replace(/^(\d+)(?:(\.\d{0,6})[\d.]*)?/, '$1$2')
      .replace(/^0+(\d)/, '$1');
  }

}

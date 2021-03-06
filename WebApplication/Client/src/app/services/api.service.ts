import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs/Observable';
import { ErrorObservable } from 'rxjs/observable/ErrorObservable';
import { catchError, shareReplay } from 'rxjs/operators';
import 'rxjs/add/observable/fromEvent';
import 'rxjs/add/observable/merge';
import 'rxjs/add/operator/retry';

import { environment } from '../../environments/environment';
import {Subject} from "rxjs/Subject";
import {ActivatedRoute, Router} from "@angular/router";


@Injectable()
export class APIService {

  private _sumusBaseUrl = environment.sumusNetworkUrl;
  private _marketBaseUrl = environment.marketApiUrl;
  public networkList = {
    mainnet: 'mainnet',
    testnet: 'testnet'
  };

  public transferTradingError$ = new Subject();
  public transferTradingLimit$ = new Subject();
  public transferCurrentNetwork = new Subject();

  constructor(
    private _http: HttpClient,
    private router: Router,
    private route: ActivatedRoute
  ) {
    this._sumusBaseUrl = environment.sumusNetworkUrl[this.networkList.mainnet];

    this.route.queryParams.subscribe(params => {
        if (params.network == this.networkList.mainnet || params.network == this.networkList.testnet) {
          this._sumusBaseUrl = environment.sumusNetworkUrl[params.network];
          localStorage.setItem('network', params.network);
        }
    });

    this.transferCurrentNetwork.subscribe((network: any) => {
      this._sumusBaseUrl = environment.sumusNetworkUrl[network];

      this.router.navigate([], {
        queryParams: { network: network == this.networkList.testnet ? network : null },
        queryParamsHandling: 'merge',
      });
    })
  }

  getGoldRate(): Observable<object> {
    return this._http
      .get('https://service.goldmint.io/info/rate/v1/gold')
      .pipe(
        catchError(this._handleError),
        shareReplay()
      );
  }

  getMntpRate(): Observable<object> {
    return this._http
      .get('https://service.goldmint.io/info/rate/v1/mntp')
      .pipe(
        catchError(this._handleError),
        shareReplay()
      );
  }

  // pawnshop

  getOrganizationList(from: number) {
    let _from = from !== null ? from : '-'
    return this._http.get(`${this._marketBaseUrl}/org/list/${_from}`);
  }

  getOrganizationDetails(id: number) {
    return this._http.get(`${this._marketBaseUrl}/org/details/${id}`);
  }

  getPawnshopList(org: number, from: number) {
    let _org = org !== null ? org : '-';
    let _from = from !== null ? from : '-';
    return this._http.get(`${this._marketBaseUrl}/pawnshop/list/${_org}/${_from}`);
  }

  getPawnList(pawnshop: number, from: number) {
    let _pawnshop = pawnshop !== null ? pawnshop : '-';
    let _from = from !== null ? from : '-';
    return this._http.get(`${this._marketBaseUrl}/pawn/list/${_pawnshop}/${_from}`);
  }

  getPawnshopDetails(id: number) {
    return this._http.get(`${this._marketBaseUrl}/pawnshop/${id}`);
  }

  getOrganizationsName() {
    return this._http.get(`${this._marketBaseUrl}/org/names`);
  }

  // scanner methods

  getScannerStatus() {
    return this._http.get(`${this._sumusBaseUrl}/status`);
  }

  getScannerDailyStatistic(useMainNet: boolean = false) {
    const sumusBaseUrl = useMainNet ? environment.sumusNetworkUrl[this.networkList.mainnet] : this._sumusBaseUrl;
    return this._http.get(`${sumusBaseUrl}/status/daily`);
  }

  getWalletBalance(sumusAddress: string) {
    return this._http.get(`${this._sumusBaseUrl}/wallet/${sumusAddress}`);
  }

  checkTransactionStatus(digest: string, network: string) {
    let _sumusBaseUrl = environment.sumusNetworkUrl[network] || this._sumusBaseUrl;
    return this._http.get(`${_sumusBaseUrl}/tx/${digest}`);
  }

  getTransactionsInBlock(blockNumber: number) {
    return this._http.get(`${this._sumusBaseUrl}/block/${blockNumber}`);
  }

  getScannerBlockList(from: number) {
    let _from: number|string = from;
    if (from == null) _from = "-";
    return this._http.get(`${this._sumusBaseUrl}/block/list/${_from}`);
  }

  getScannerTxList(block: number, address: string, from: string) {
    let _block: number|string = block;
    if (block == null) _block = "-";
    if (address == null) address = "-";
    if (from == null) from = "-";
    return this._http.get(`${this._sumusBaseUrl}/tx/list/${_block}/${address}/${from}`);
  }

  // master node

  getCurrentActiveNodesStats() {
    return this._http.get(`${this._sumusBaseUrl}/node/stats`);
  }

  getCurrentActiveNodesList(from: string) {
    return this._http.get(`${this._sumusBaseUrl}/node/list/${from || '-'}`);
  }

  getLatestRewardList(from: number) {
    return this._http.get(`${this._sumusBaseUrl}/reward/list/${from || '-'}`);
  }

  getRewardTransactions(id: number, from: number) {
    return this._http.get(`${this._sumusBaseUrl}/reward/${id}/list/${from || '-'}`);
  }

  private _handleError(err: HttpErrorResponse | any) {
    if (err.error && err.error.errorCode) {
      switch (err.error.errorCode) {
        default:
          break;
      }
    }
    else {
      if (!err.message) {
        err.message = 'Unable to retrieve data';
      }
    }

    return ErrorObservable.create(err);
  }

}

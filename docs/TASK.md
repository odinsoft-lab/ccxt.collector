# Exchange Implementation Status

This document tracks the implementation status by region compared to ccxt/ccxt C# exchanges.

Legend: FULL = implemented in this repo, TODO = not implemented yet, CLOSED = exchange shut down.

*Last Updated: 2026-01-12*

## CN (China / HK based)
- binance: FULL
- bigone: TODO
- bingx: TODO
- bitforex: TODO
- bitget: FULL
- bybit: FULL
- coinex: TODO
- digifinex: TODO
- gate: TODO
- gateio: FULL
- hashkey: TODO
- hitbtc: TODO
- htx: TODO
- huobi: FULL
- kucoin: FULL
- kucoinfutures: TODO
- lbank: TODO
- mexc: PARTIAL (requires protobuf support - connection works, data parsing pending)
- okx: FULL
- woo: TODO
- woofipro: TODO
- xt: TODO
- zb: TODO

## US
- alpaca: TODO
- apex: TODO
- ascendex: TODO
- binancecoinm: TODO
- binanceus: TODO
- binanceusdm: TODO
- bittrex: CLOSED (December 2023)
- coinbase: FULL
- coinbaseadvanced: TODO
- coinbaseexchange: TODO
- coinbaseinternational: TODO
- crypto: FULL
- cryptocom: FULL
- gdax: TODO
- gemini: TODO
- itbit: TODO
- kraken: FULL
- krakenfutures: TODO
- okcoin: TODO
- okxus: TODO
- paradex: TODO
- phemex: TODO
- poloniex: TODO
- vertex: TODO

## KR (Korea)
- bithumb: FULL
- coinone: FULL
- gopax: FULL
- korbit: FULL
- okcoinkr: TODO
- probit: TODO
- upbit: FULL

## GB (United Kingdom)
- bitfinex: FULL
- bitstamp: FULL
- bitteam: TODO
- blockchaincom: TODO
- cexio: TODO
- coinmetro: TODO
- luno: TODO

## AE (UAE)
- deribit: TODO

## AU (Australia)
- btcmarkets: TODO
- coinspot: TODO

## BR (Brazil)
- foxbit: TODO
- mercado: TODO
- novadax: TODO

## BS (Bahamas)
- fmfwio: TODO

## CA (Canada)
- ndax: TODO
- timex: TODO

## EE (Estonia)
- latoken: TODO

## EU (Europe)
- bit2c: TODO
- bitopro: TODO
- bitvavo: TODO
- btcalpha: TODO
- btcturk: TODO
- coinmate: TODO
- exmo: TODO
- onetrading: TODO
- paymium: TODO
- wavesexchange: TODO
- whitebit: TODO
- yobit: TODO
- zonda: TODO

## ID (Indonesia)
- indodax: TODO
- tokocrypto: TODO

## IN (India)
- bitbns: TODO
- modetrade: TODO

## JP (Japan)
- anxpro: TODO
- bitbank: TODO
- bitflyer: TODO
- bittrade: TODO
- btcbox: TODO
- coincheck: TODO
- quoinex: TODO
- zaif: TODO

## KY (Cayman Islands)
- bitmart: TODO
- blofin: TODO

## LT (Lithuania)
- cryptomus: TODO

## MT (Malta)
- bequant: TODO

## MX (Mexico)
- bitso: TODO

## SC (Seychelles)
- bitmex: TODO

## SG (Singapore)
- bitrue: TODO
- coinsph: TODO
- delta: TODO
- derive: TODO
- ellipx: TODO
- hibachi: TODO
- hyperliquid: TODO
- independentreserve: TODO

## GLOBAL
- coincatch: TODO
- defx: TODO
- hollaex: TODO
- myokx: TODO
- oceanex: TODO
- oxfun: TODO
- p2b: TODO
- tradeogre: TODO

Notes:
- Status is derived from presence of exchange-specific WebSocket clients in `src/exchanges/{region}/{exchange}`. If additional REST-only implementations exist, please update accordingly.
- This list mirrors ccxt/ccxt C# directory names as of today; if ccxt adds/removes exchanges, update this file.

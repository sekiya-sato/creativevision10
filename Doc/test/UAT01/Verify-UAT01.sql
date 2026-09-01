.headers on
.mode column

SELECT Id, json_extract(value, '$.TaxRate') AS TaxRate,
       json_extract(value, '$.DateFrom') AS DateFrom,
       json_extract(value, '$.TaxNewRate') AS TaxNewRate
FROM MasterSysman, json_each(Jsub)
WHERE MasterSysman.Id=1 AND json_extract(value, '$.Id')=1;

SELECT Id, Code, Name, Shime1, PayMonth, PayDay, IsPay
FROM MasterShiire WHERE Code='UAT01-SI';

SELECT Id, Code, Name FROM MasterTokui WHERE Code='UAT01-SK';
SELECT Id, Code, Name FROM MasterMeisho WHERE Kubun='MKR' AND Code='UAT01-SI';
SELECT Id, Code, Name, Id_Maker, Id_Soko, Id_Tax
FROM MasterShohin WHERE Code='UAT01-P01';

SELECT Id, Id_Shohin, RowIdx, Code, Id_Col, Id_Siz, Jan1
FROM DerivedShohinColSiz WHERE Id=7893301;

SELECT Id, DenDay, EndFlag, KingakuTotal, Total, Tax1+Tax2+Tax3 AS Tax
FROM Tran13Hachu WHERE Id IN (3,4) ORDER BY Id;

SELECT Id, DenDay, Kubun, RelateNo1, KingakuTotal, Total, Tax1+Tax2+Tax3 AS Tax, CalcFlag
FROM Tran03Shiire WHERE Id BETWEEN 26 AND 30 ORDER BY Id;

SELECT Id, KakeDay, Id_Shain, Id_Torisaki, KingakuTotal, Jmeisai
FROM Tran07Shiharai WHERE Id=5;

SELECT Id_Soko, Id_Shohin, Id_Col, Id_Siz, Su, ReserveQty
FROM SummaryRealStock
WHERE Id_Soko=2813 AND Id_Shohin=78933 AND Id_Col=2757 AND Id_Siz=233;

SELECT Id_Shiire, DenMonth, Balance, TotalOut, TotalShiire,
       Shiire, Henpin, Tax1+Tax2+Tax3 AS Tax, Cash
FROM SummaryKaiKake WHERE Id_Shiire=502 AND DenMonth='202609';

SELECT Id_Shiire, DenDay, DayFrom, DayTo, ShiharaiYoteiDay,
       Balance, TotalOut, TotalShiire, Shiire, Henpin, Tax1+Tax2+Tax3 AS Tax, Cash
FROM SummaryKaiShi WHERE Id_Shiire=502 AND DenDay='20260930';

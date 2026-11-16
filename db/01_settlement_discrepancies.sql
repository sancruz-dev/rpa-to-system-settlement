/* New object introduced by this project on top of the pre-existing settlement schema
   (ArquivosLiquidacao / LiquidacoesImportadas / Operacoes / Clientes / Ativos).
   Persists the discrepancies found by the reconciliation step so they can be queried
   and monitored, instead of only existing as an ad-hoc SELECT result. */

IF OBJECT_ID('dbo.SettlementDiscrepancies') IS NULL
BEGIN
    CREATE TABLE dbo.SettlementDiscrepancies (
        Id              BIGINT IDENTITY(1,1) CONSTRAINT PK_SettlementDiscrepancies PRIMARY KEY,
        ArquivoId       INT NOT NULL
                        CONSTRAINT FK_SettlementDiscrepancies_Arquivos REFERENCES dbo.ArquivosLiquidacao (ArquivoId),
        Document        VARCHAR(14)   NOT NULL,
        Ticker          VARCHAR(10)   NOT NULL,
        ExpectedAmount  DECIMAL(18,2) NULL,
        ReceivedAmount  DECIMAL(18,2) NULL,
        DiscrepancyType VARCHAR(20)   NOT NULL
                        CONSTRAINT CK_SettlementDiscrepancies_Type
                        CHECK (DiscrepancyType IN ('Missing', 'Unexpected', 'AmountMismatch')),
        DetectedAt      DATETIME2(0)  NOT NULL
                        CONSTRAINT DF_SettlementDiscrepancies_DetectedAt DEFAULT (SYSDATETIME())
    );

    CREATE NONCLUSTERED INDEX IX_SettlementDiscrepancies_ArquivoId
        ON dbo.SettlementDiscrepancies (ArquivoId);
END
GO

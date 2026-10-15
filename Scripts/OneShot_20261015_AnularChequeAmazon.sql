/*
    OneShot 15/10/26 — NestoAPI#593: AMAZON EU (29440) no tiene que recibir cheque regalo (se nos pasó: solo estaba
    excluido el 32624). Desde el commit de hoy la API excluye 29440 y 31482; este script anula el que ya se generó (Id 31).
    Ejecutar en SSMS contra NV como sa. Solo lo anula si sigue sin canjear.
*/
SET NOCOUNT ON;
UPDATE dbo.ChequesRegalo SET Estado = 'Anulado', Usuario = 'OneShot_20261015', FechaModificacion = GETDATE()
WHERE Id = 31 AND RTRIM(Cliente) = '29440' AND Estado = 'Generado' AND PedidoCanje IS NULL;
SELECT Id, Cliente, Estado, PedidoCanje FROM dbo.ChequesRegalo WHERE Id = 31;

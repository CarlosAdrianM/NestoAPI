/*
    NestoAPI#622 — arreglo 15/10/26 (sugerencia 619 de Paloma: «No puedo traspasar la reposición»).
    Ejecutar en SSMS contra NV como sa. Idempotente. Efecto inmediato: no hace falta publicar nada.

    QUÉ PASA
    Desde #622, cuando el corte de las 9:00 se suma a una preparación que la tienda tenía sin enviar, se crea una
    segunda cabecera enlazada (IdCompletada = la primera). Al «Terminar y mandar», la API pone el MISMO número de
    traspaso a las dos (una preparación completada en varios cortes es un solo traspaso), pero el índice único
    UX_ReposicionesTraspasos_Traspaso (Empresa, NumTraspaso) no deja dos filas con el mismo número → error
    «No se puede insertar una fila de clave duplicada… (1, 81003)» y la transacción entera se deshace: ALC no puede
    enviar (cabeceras 7 y 14, ALC→ALG).

    ARREGLO
    El número sigue siendo único entre las cabeceras PRINCIPALES (IdCompletada IS NULL); las que completan una
    preparación comparten el número de su principal. El resto del código ya trata varias filas con el mismo número
    (marcar recibida/preparada por número, borrar por número, TOP 1 al leer).
*/
SET NOCOUNT ON;

IF COL_LENGTH('dbo.ReposicionesTraspasos', 'IdCompletada') IS NULL
BEGIN
    RAISERROR('Falta la columna IdCompletada (Scripts/Issue622_ReposicionesTraspasos_IdCompletada.sql): no hay nada que arreglar.', 10, 1);
    RETURN;
END

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ReposicionesTraspasos')
           AND name = 'UX_ReposicionesTraspasos_Traspaso' AND filter_definition NOT LIKE '%IdCompletada%')
    DROP INDEX UX_ReposicionesTraspasos_Traspaso ON dbo.ReposicionesTraspasos;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.ReposicionesTraspasos') AND name = 'UX_ReposicionesTraspasos_Traspaso')
    EXEC sp_executesql N'CREATE UNIQUE INDEX UX_ReposicionesTraspasos_Traspaso ON dbo.ReposicionesTraspasos (Empresa, NumTraspaso)
        WHERE NumTraspaso IS NOT NULL AND IdCompletada IS NULL';

SELECT name, filter_definition FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.ReposicionesTraspasos') AND name = 'UX_ReposicionesTraspasos_Traspaso';

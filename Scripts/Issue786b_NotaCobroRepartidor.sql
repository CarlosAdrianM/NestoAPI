/*
    NestoAPI#786 (ampliación, decisión de Carlos 04/11/26): la nota de cobro se ASIGNA a un repartidor al crearla y solo él la ve en
    «Mis envíos» de Ariadna (GET api/RepartoPropio/Envios?soloMios=true). Hasta ahora, como nadie la carga en la furgoneta, salía a
    todos los repartidores (como las paradas sin repartidor).

    - NotasEntregaCabecera.Repartidor: el usuario sin dominio («Miguel») de GET api/RepartoPropio/Repartidores. Solo en las de cobro
      (NULL en las de entrega y recogida, y en las de cobro creadas antes de este script: esas siguen saliendo a todos).
    - Sin este script, POST api/NotasEntrega/Cobro contesta 400 «falta lanzar el script Scripts/Issue786b_NotaCobroRepartidor.sql» y
      la lista del reparto sigue como antes.

    Requiere Scripts/Issue786_NotasCobro.sql. Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    Sin GRANT nuevo: la API (NUEVAVISION\RDS2016$) ya lee e inserta en NotasEntregaCabecera (Issue757).
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.NotasEntregaCabecera', 'ImporteCobro') IS NULL
BEGIN
    RAISERROR('Falta NotasEntregaCabecera.ImporteCobro: lance antes Scripts/Issue786_NotasCobro.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH('dbo.NotasEntregaCabecera', 'Repartidor') IS NULL
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera ADD Repartidor varchar(50) NULL;
    PRINT 'NotasEntregaCabecera.Repartidor: creada.';
END
ELSE
    PRINT 'NotasEntregaCabecera.Repartidor: ya existía.';
GO

SET NOEXEC OFF;
GO

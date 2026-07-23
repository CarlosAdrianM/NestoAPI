-- ===========================================================================
-- Script: Issue356_prdContabilizar_truncar_cif.sql
-- Fecha:  23/07/2026
-- Issue:  NestoAPI#356 (parche A - puente)
--
-- CONTEXTO: esta noche se amplio Clientes.[CIF/NIF] a varchar(20). El unico modulo
-- que ESCRIBE en ExtractoCliente.[CIF/NIF] (char(9)) es prdContabilizar, a traves de su
-- variable @CIF, declarada varchar(20). Al facturar un cliente con CIF > 9 caracteres,
-- @CIF trae el valor completo e intenta meterlo en la columna char(9) -> error 8152
-- ("String or binary data would be truncated") -> prdContabilizar hace ROLLBACK y la
-- factura NO se emite (ver prdCrearFacturaVta -> prdContabilizar).
--
-- FIX (puente, sin reconstruir la tabla de 6 GB): declarar @CIF como varchar(9). Asi la
-- asignacion 'select @CIF = [CIF/NIF] from Clientes' TRUNCA a 9 en todas las ramas
-- (factura/cartera/pago/impagado) y el insert en ExtractoCliente cabe. La copia en
-- ExtractoCliente queda a 9 chars, lo cual es aceptable: es una copia denormalizada que
-- Verifactu NO lee (lee de CabFacturaVta.CifNif, ya varchar(20)); solo afecta al CIF de
-- un extranjero en el informe de IRPF/347.
--
-- La rama de PROVEEDOR (@CIF = [CIF/NIF] from Proveedores -> ExtractoProveedor char(9))
-- NO se ve afectada: Proveedores.[CIF/NIF] es char(9) y no hay proveedores con CIF > 9.
--
-- SEGURIDAD: se hace TODO en el servidor (los acentos/ñ nunca salen de la BD) mediante
-- REPLACE sobre la definicion + sp_executesql. Aserciones previas: exige exactamente 1
-- '@CIF varchar(20)' y 1 'create proc'; idempotente si ya esta a varchar(9). Transaccion
-- con verificacion: si algo falla, rollback y NADA aplicado.
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

DECLARE @d nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdContabilizar'));
IF @d IS NULL
BEGIN
    RAISERROR('No existe dbo.prdContabilizar. Abortado.', 16, 1);
    RETURN;
END

DECLARE @nCif20 int = (LEN(@d) - LEN(REPLACE(@d, '@CIF varchar(20)', ''))) / LEN('@CIF varchar(20)');
DECLARE @nCif9  int = (LEN(@d) - LEN(REPLACE(@d, '@CIF varchar(9)',  ''))) / LEN('@CIF varchar(9)');
DECLARE @nProc  int = (LEN(@d) - LEN(REPLACE(@d, 'create proc',      ''))) / LEN('create proc');

-- Idempotencia
IF @nCif9 >= 1 AND @nCif20 = 0
BEGIN
    PRINT 'IDEMPOTENCIA: @CIF ya es varchar(9) en prdContabilizar. Nada que hacer.';
    RETURN;
END

-- Aserciones de seguridad (si el SP cambia en el futuro, este script aborta sin tocar nada)
IF @nCif20 <> 1
BEGIN
    RAISERROR('Se esperaba exactamente 1 "@CIF varchar(20)" y se han encontrado %d. Abortado (revisar el SP a mano).', 16, 1, @nCif20);
    RETURN;
END
IF @nProc <> 1
BEGIN
    RAISERROR('Se esperaba exactamente 1 "create proc" y se han encontrado %d. Abortado.', 16, 1, @nProc);
    RETURN;
END

-- Construir la version ALTER (solo se tocan trozos ASCII; el resto byte a byte intacto)
DECLARE @alter nvarchar(max) = @d;
SET @alter = REPLACE(@alter, '@CIF varchar(20)', '@CIF varchar(9)');
SET @alter = REPLACE(@alter, 'create proc', 'ALTER PROC');   -- colacion CI: casa create/CREATE; solo hay 1

-- Preview de la linea afectada
DECLARE @pp int = CHARINDEX('@CIF varchar(9)', @alter);
PRINT '=== Declaracion tras el cambio (preview) ===';
PRINT SUBSTRING(@alter, @pp - 20, 90);

BEGIN TRAN;
    EXEC sys.sp_executesql @alter;

    -- Verificacion dentro de la misma transaccion
    DECLARE @v nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdContabilizar'));
    IF (LEN(@v) - LEN(REPLACE(@v, '@CIF varchar(9)', ''))) / LEN('@CIF varchar(9)') = 1
       AND (LEN(@v) - LEN(REPLACE(@v, '@CIF varchar(20)', ''))) / LEN('@CIF varchar(20)') = 0
    BEGIN
        COMMIT TRAN;
        PRINT 'OK: prdContabilizar recompilado con @CIF varchar(9). Los CIF > 9 se truncan al escribir en ExtractoCliente y la factura ya no falla.';
    END
    ELSE
    BEGIN
        ROLLBACK TRAN;
        RAISERROR('VERIFICACION FALLIDA: la definicion vigente no muestra @CIF varchar(9). Rollback, NADA aplicado.', 16, 1);
    END
GO

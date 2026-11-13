/*
    NestoAPI#860: el prepago se contabiliza con SU fecha (la del cobro), en un asiento aparte del de la factura.

    Lanzar como sa, en la BD NV, DESPUÉS de Scripts/Issue860_Prepagos_FechaCobro.sql (la columna Prepagos.FechaCobro).
    Mejor fuera de horario: cambia los dos procedimientos de facturación.

    Qué cambia
    ----------
    prdCrearFacturaVta
      - Antes: el cobro del prepago (pago del cliente contra la cuenta del prepago) iba en el asiento de la factura,
        con la fecha de la factura.
      - Ahora: la factura se contabiliza sin el cobro y, a continuación, el cobro va en su propio asiento
        (segunda llamada a prdContabilizar), con fecha = Prepagos.FechaCobro. Se usa la fecha de la factura si
        FechaCobro es NULL, si es posterior a la factura o si el usuario no puede contabilizar en esa fecha
        (prdFechaEsVálida, contabilidad y, si la cuenta es 572, banco): un prepago nunca impide facturar.
        Prepagos del mismo pedido con fechas distintas: un asiento por fecha. Después se liquida cada pago
        contra el efecto de la factura, como antes.
    prdDeshacerFacturaVta
      - Además del asiento de la factura, desliquida y borra los asientos del cobro del prepago de esa factura
        (ExtractoCliente y Contabilidad del diario _FRAVTA con ese documento).

    Cómo lo hace (y por qué así)
    ----------------------------
    No se reescriben los procedimientos enteros: se toma la definición ACTUAL de cada uno, se comprueba que los
    puntos de anclaje están exactamente una vez, se insertan los tramos nuevos y se hace ALTER. Antes de tocar nada:
      1. Guarda la definición anterior en dbo.Issue860_ProcedimientosAnteriores (para volver atrás).
      2. Ensaya la compilación creando una copia con otro nombre (…_Ensayo860) y la borra.
    Si algo no cuadra (anclaje que falta o repetido, columna sin crear, error de compilación) NO cambia nada y lo dice.
    Es idempotente: si el procedimiento ya lleva la marca, lo deja como está.

    Volver atrás
    ------------
    Ver el final del fichero (bloque comentado «VOLVER ATRÁS»).
*/
SET NOCOUNT ON;
GO

IF COL_LENGTH('dbo.Prepagos', 'FechaCobro') IS NULL
BEGIN
    RAISERROR('Falta la columna Prepagos.FechaCobro: lance antes Scripts/Issue860_Prepagos_FechaCobro.sql. No se ha cambiado nada.', 16, 1);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID('dbo.Issue860_ProcedimientosAnteriores', 'U') IS NULL
    CREATE TABLE dbo.Issue860_ProcedimientosAnteriores
    (
        Id int IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Procedimiento sysname NOT NULL,
        Fecha datetime NOT NULL DEFAULT (GETDATE()),
        Usuario sysname NOT NULL DEFAULT (SYSTEM_USER),
        Definicion nvarchar(max) NOT NULL
    );
GO

------------------------------------------------------------------------------------------------------------
-- 1) prdCrearFacturaVta
------------------------------------------------------------------------------------------------------------
DECLARE @nombre sysname, @marca nvarchar(100), @def nvarchar(max), @nuevo nvarchar(max), @ensayo nvarchar(max);
DECLARE @ancla1 nvarchar(400), @ancla2 nvarchar(400), @tramo1 nvarchar(max), @tramo2 nvarchar(max);
DECLARE @p int, @q int, @veces1 int, @veces2 int, @error nvarchar(2048);

SET @nombre = N'prdCrearFacturaVta';
SET @marca = N'NestoAPI#860: prepago en asiento aparte';
SET @def = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdCrearFacturaVta'));

-- Anclaje 1: la línea que prepara el concepto del prepago, justo antes del tramo antiguo que lo metía en el asiento de la factura.
SET @ancla1 = N'set @conceptoPrepago = left(''Prepago ''+ @conceptoPrepago, 50)';
-- Anclaje 2: el comentario que abre la liquidación de la factura (la factura ya está contabilizada en @UltAsiento).
SET @ancla2 = N'-- Liquidar (17/06/02)';

SET @tramo1 = @ancla1 + NCHAR(13) + NCHAR(10) + N'
-- ' + @marca + N' (13/11/26). El cobro ya NO va en el asiento de la factura:
-- se guarda el prepago en @idPrepago860 y se anula @idPrepago para que los dos tramos antiguos (el que lo metía en la
-- PreContabilidad de la factura y el que lo liquidaba) no hagan nada. El cobro se contabiliza más abajo, con su fecha.
declare @idPrepago860 int
set @idPrepago860 = @idPrepago
set @idPrepago = null
';

SET @tramo2 = N'-- ' + @marca + N' (13/11/26). La factura ya está contabilizada (@UltAsiento).
-- Ahora el cobro del prepago, en su propio asiento y con su fecha (Prepagos.FechaCobro; sin ella, la de la factura).
if @idPrepago860 is not null begin
	declare @Prepagos860 table (Id int primary key, FechaAsiento datetime, NumAsiento int)
	insert into @Prepagos860 (Id, FechaAsiento)
		select Id, case when FechaCobro is null or datediff(day, FechaCobro, @Fecha) <= 0 then @Fecha else cast(FechaCobro as datetime) end
		from Prepagos
		where Empresa = @Empresa and Pedido = @Pedido and Factura is null and Importe != 0

	-- Una fecha de cobro en la que el usuario no puede contabilizar (periodo cerrado) o, si va a banco, que no vale
	-- para banco, pasa a ser la de la factura: un prepago nunca impide facturar.
	declare @FechaCobro860 datetime, @Vale860 int
	declare crsFechas860 cursor local fast_forward for select distinct FechaAsiento from @Prepagos860 where FechaAsiento <> @Fecha
	open crsFechas860
	fetch next from crsFechas860 into @FechaCobro860
	while @@fetch_status = 0 begin
		exec @Vale860 = prdFechaEsVálida @Empresa, @FechaCobro860, 1, @Usuario
		if @Vale860 = 1 and exists (select * from @Prepagos860 t inner join Prepagos p on p.Id = t.Id where t.FechaAsiento = @FechaCobro860 and p.CuentaContable like ''572%'')
			exec @Vale860 = prdFechaEsVálida @Empresa, @FechaCobro860, 6, @Usuario
		if isnull(@Vale860, 0) <> 1
			update @Prepagos860 set FechaAsiento = @Fecha where FechaAsiento = @FechaCobro860
		fetch next from crsFechas860 into @FechaCobro860
	end
	close crsFechas860
	deallocate crsFechas860

	-- Un asiento por fecha
	update t set NumAsiento = r.Orden
	from @Prepagos860 t inner join (select Id, dense_rank() over (order by FechaAsiento) as Orden from @Prepagos860) r on r.Id = t.Id

	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,efecto,delegación,formaventa,Ruta, Origen, Contrapartida, Usuario)
		select p.Empresa, 3, 2, @NumCliente, @ContactoCobro, left(''Prepago ''+ p.ConceptoAdicional, 50), p.Importe, t.FechaAsiento, @NumFactura, t.NumAsiento,''_FRAVTA'',1,t.FechaAsiento,@Formapago,@vendedorReal,1,@DelegaciónCab,@FormaVentaCab,@RutaCobro, @Origen, p.CuentaContable, @Usuario
		from Prepagos p inner join @Prepagos860 t on t.Id = p.Id
	if @@error != 0 begin
		if @@trancount > 0 rollback
		set @NumFactura=''''
		raiserror(''No se ha podido preparar el asiento del prepago'',11,1)
		return(-1)
	end

	declare @AsientoPrepago860 int
	exec @AsientoPrepago860 = prdContabilizar @Empresa, ''_FRAVTA'', @Usuario
	-- prdContabilizar, si falla, ya deshace la transacción: por eso se mira @@trancount antes de deshacer
	if @@error != 0 or isnull(@AsientoPrepago860, 0) <= 0 begin
		if @@trancount > 0 rollback
		set @NumFactura=''''
		raiserror(''Error al contabilizar el prepago'',16,1)
		return(-1)
	end

	update Prepagos set Factura = @NumFactura where Id in (select Id from @Prepagos860)

	-- Liquidamos cada pago del prepago contra el efecto de la factura que siga pendiente
	declare @Pago860 int, @Efecto860 int
	declare crsPagos860 cursor local fast_forward for
		select [nº orden] from extractocliente
		where empresa = @empresa and [nº documento] = @NumFactura and tipoapunte = 3 and asiento > @UltAsiento and asiento <= @AsientoPrepago860
		order by [nº orden]
	open crsPagos860
	fetch next from crsPagos860 into @Pago860
	while @@fetch_status = 0 begin
		set @Efecto860 = null
		select top 1 @Efecto860 = [nº orden] from extractocliente
		where empresa = @empresa and asiento = @UltAsiento and tipoapunte = 2 and ImportePdte <> 0
		order by [nº orden]
		if @Efecto860 is not null
			exec prdLiquidar @Empresa, @Efecto860, @Pago860
		fetch next from crsPagos860 into @Pago860
	end
	close crsPagos860
	deallocate crsPagos860
end

' + @ancla2;

IF @def IS NULL
    RAISERROR('No se encuentra dbo.prdCrearFacturaVta. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(@marca, @def) > 0
    PRINT 'prdCrearFacturaVta ya lleva el cambio de NestoAPI#860: se deja como está.';
ELSE
BEGIN
    SET @veces1 = (DATALENGTH(@def) - DATALENGTH(REPLACE(@def, @ancla1, N''))) / DATALENGTH(@ancla1);
    SET @veces2 = (DATALENGTH(@def) - DATALENGTH(REPLACE(@def, @ancla2, N''))) / DATALENGTH(@ancla2);
    SET @p = PATINDEX(N'%CREATE %PROCEDURE %', @def);
    SET @q = CASE WHEN @p > 0 THEN CHARINDEX(N'PROCEDURE', @def, @p) ELSE 0 END;

    IF @veces1 <> 1 OR @veces2 <> 1
        RAISERROR('prdCrearFacturaVta: los anclajes no están exactamente una vez (concepto del prepago: %d; liquidar: %d). El procedimiento ha cambiado: no se ha tocado nada.', 16, 1, @veces1, @veces2);
    ELSE IF @p = 0 OR @q = 0 OR LTRIM(RTRIM(SUBSTRING(@def, @p + 6, @q - @p - 6))) <> N''
        RAISERROR('prdCrearFacturaVta: no se reconoce la cabecera CREATE PROCEDURE. No se ha tocado nada.', 16, 1);
    ELSE
    BEGIN
        SET @nuevo = REPLACE(REPLACE(@def, @ancla1, @tramo1), @ancla2, @tramo2);
        BEGIN TRY
            -- Ensayo de compilación con otro nombre
            SET @ensayo = STUFF(@nuevo, @q, CHARINDEX(N'@', @nuevo, @q) - @q, N'PROCEDURE dbo.prdCrearFacturaVta_Ensayo860 ');
            IF OBJECT_ID('dbo.prdCrearFacturaVta_Ensayo860', 'P') IS NOT NULL DROP PROCEDURE dbo.prdCrearFacturaVta_Ensayo860;
            EXEC (@ensayo);
            DROP PROCEDURE dbo.prdCrearFacturaVta_Ensayo860;

            INSERT INTO dbo.Issue860_ProcedimientosAnteriores (Procedimiento, Definicion) VALUES (@nombre, @def);
            SET @nuevo = STUFF(@nuevo, @p, 6, N'ALTER');
            EXEC (@nuevo);
            PRINT 'prdCrearFacturaVta cambiado: el prepago va en su propio asiento, con la fecha del cobro.';
        END TRY
        BEGIN CATCH
            SET @error = ERROR_MESSAGE();
            IF OBJECT_ID('dbo.prdCrearFacturaVta_Ensayo860', 'P') IS NOT NULL DROP PROCEDURE dbo.prdCrearFacturaVta_Ensayo860;
            RAISERROR('prdCrearFacturaVta NO se ha cambiado (o revise si se quedó a medias): %s', 16, 1, @error);
        END CATCH
    END
END
GO

------------------------------------------------------------------------------------------------------------
-- 2) prdDeshacerFacturaVta
------------------------------------------------------------------------------------------------------------
DECLARE @nombre sysname, @marca nvarchar(100), @def nvarchar(max), @nuevo nvarchar(max), @ensayo nvarchar(max);
DECLARE @ancla nvarchar(400), @tramo nvarchar(max);
DECLARE @p int, @q int, @veces int, @error nvarchar(2048);

SET @nombre = N'prdDeshacerFacturaVta';
SET @marca = N'NestoAPI#860: prepago en asiento aparte';
SET @def = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerFacturaVta'));

-- Anclaje: el comentario que precede a «update Prepagos set Factura = null» (ya se ha borrado el asiento de la factura).
SET @ancla = N'-- Carlos 27/08/20: modificamos el prepago';

SET @tramo = N'-- ' + @marca + N' (13/11/26). El cobro del prepago está en otro asiento (o en varios,
-- uno por fecha): los del diario _FRAVTA con este documento y un pago del cliente, distintos del asiento de la factura.
declare @AsientosPrepago860 table (Asiento int primary key)
insert into @AsientosPrepago860 (Asiento)
	select distinct e.Asiento
	from ExtractoCliente e
	where e.Empresa = @Empresa and e.[Nº Documento] = @NumFactura and e.TipoApunte = 3 and e.Asiento <> @Asiento
		and exists (select * from Contabilidad c where c.Empresa = e.Empresa and c.Asiento = e.Asiento and c.Diario = ''_FRAVTA'' and c.[Nº Documento] = @NumFactura)

if exists (select * from @AsientosPrepago860) begin
	declare crsPrepago860 cursor local fast_forward for
		select [Nº Orden], Importe, ImportePdte from ExtractoCliente where Empresa = @Empresa and Asiento in (select Asiento from @AsientosPrepago860)
	open crsPrepago860
	fetch next from crsPrepago860 into @NumOrden,@Importe,@ImportePdte
	while @@fetch_status = 0 begin
		if @Importe != @ImportePdte begin
			exec @Devuelve = prdDesliquidar @Empresa, @NumOrden
			if @Devuelve < 0 begin
				raiserror (''Se ha producido un error al desliquidar el prepago %d'',11,1,@NumOrden)
				if @@trancount > 0 rollback transaction
				return -1
			end
		end
		fetch next from crsPrepago860 into @NumOrden,@Importe,@ImportePdte
	end
	close crsPrepago860
	deallocate crsPrepago860

	delete ExtractoCliente where Empresa = @Empresa and Asiento in (select Asiento from @AsientosPrepago860)
	if @@error != 0 begin
		raiserror(''No se pueden borrar los movimientos del prepago. Imposible deshacer factura'',11,1)
		if @@trancount > 0 rollback transaction
		return -1
	end
	delete Contabilidad where Empresa = @Empresa and Asiento in (select Asiento from @AsientosPrepago860)
	if @@error != 0 begin
		raiserror(''No se puede borrar la contabilidad del prepago. Imposible deshacer factura'',11,1)
		if @@trancount > 0 rollback transaction
		return -1
	end
end

' + @ancla;

IF @def IS NULL
    RAISERROR('No se encuentra dbo.prdDeshacerFacturaVta. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(@marca, @def) > 0
    PRINT 'prdDeshacerFacturaVta ya lleva el cambio de NestoAPI#860: se deja como está.';
ELSE
BEGIN
    SET @veces = (DATALENGTH(@def) - DATALENGTH(REPLACE(@def, @ancla, N''))) / DATALENGTH(@ancla);
    SET @p = PATINDEX(N'%CREATE %PROCEDURE %', @def);
    SET @q = CASE WHEN @p > 0 THEN CHARINDEX(N'PROCEDURE', @def, @p) ELSE 0 END;

    IF @veces <> 1
        RAISERROR('prdDeshacerFacturaVta: el anclaje no está exactamente una vez (%d). El procedimiento ha cambiado: no se ha tocado nada.', 16, 1, @veces);
    ELSE IF @p = 0 OR @q = 0 OR LTRIM(RTRIM(SUBSTRING(@def, @p + 6, @q - @p - 6))) <> N''
        RAISERROR('prdDeshacerFacturaVta: no se reconoce la cabecera CREATE PROCEDURE. No se ha tocado nada.', 16, 1);
    ELSE
    BEGIN
        SET @nuevo = REPLACE(@def, @ancla, @tramo);
        BEGIN TRY
            SET @ensayo = STUFF(@nuevo, @q, CHARINDEX(N'@', @nuevo, @q) - @q, N'PROCEDURE dbo.prdDeshacerFacturaVta_Ensayo860 ');
            IF OBJECT_ID('dbo.prdDeshacerFacturaVta_Ensayo860', 'P') IS NOT NULL DROP PROCEDURE dbo.prdDeshacerFacturaVta_Ensayo860;
            EXEC (@ensayo);
            DROP PROCEDURE dbo.prdDeshacerFacturaVta_Ensayo860;

            INSERT INTO dbo.Issue860_ProcedimientosAnteriores (Procedimiento, Definicion) VALUES (@nombre, @def);
            SET @nuevo = STUFF(@nuevo, @p, 6, N'ALTER');
            EXEC (@nuevo);
            PRINT 'prdDeshacerFacturaVta cambiado: deshace también el asiento del cobro del prepago.';
        END TRY
        BEGIN CATCH
            SET @error = ERROR_MESSAGE();
            IF OBJECT_ID('dbo.prdDeshacerFacturaVta_Ensayo860', 'P') IS NOT NULL DROP PROCEDURE dbo.prdDeshacerFacturaVta_Ensayo860;
            RAISERROR('prdDeshacerFacturaVta NO se ha cambiado (o revise si se quedó a medias): %s', 16, 1, @error);
        END CATCH
    END
END
GO

SET NOEXEC OFF;
GO

/*
    VOLVER ATRÁS (como sa). Recupera la definición guardada antes del cambio:

    DECLARE @sql nvarchar(max), @p int;
    SELECT TOP 1 @sql = Definicion FROM dbo.Issue860_ProcedimientosAnteriores WHERE Procedimiento = 'prdCrearFacturaVta' ORDER BY Id DESC;
    SET @p = PATINDEX(N'%CREATE %PROCEDURE %', @sql);
    SET @sql = STUFF(@sql, @p, 6, N'ALTER');
    EXEC (@sql);

    SELECT TOP 1 @sql = Definicion FROM dbo.Issue860_ProcedimientosAnteriores WHERE Procedimiento = 'prdDeshacerFacturaVta' ORDER BY Id DESC;
    SET @p = PATINDEX(N'%CREATE %PROCEDURE %', @sql);
    SET @sql = STUFF(@sql, @p, 6, N'ALTER');
    EXEC (@sql);

    OJO: una factura hecha con el procedimiento nuevo tiene el cobro del prepago en otro asiento; para deshacerla hace
    falta el prdDeshacerFacturaVta NUEVO. Si se vuelve atrás, volver solo prdCrearFacturaVta y dejar el de deshacer.
*/

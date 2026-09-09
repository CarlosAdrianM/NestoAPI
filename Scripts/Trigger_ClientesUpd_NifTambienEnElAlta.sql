/*
    trgClientesUpd - devolver la comprobacion de CIF/NIF duplicado a las ALTAS.

    QUE PASO
    --------
    El script Trigger_ClientesUpd_NifSoloSiCambia (08/09/26) hizo que la comprobacion de NIF
    duplicado exigiera que el NIF hubiera CAMBIADO, comparando `inserted` contra `deleted`. Eso
    arreglo lo que tenia que arreglar -26 clientes con la ficha congelada- pero **abrio un agujero
    que no estaba previsto**:

        el disparador es FOR insert, UPDATE, y en un INSERT la tabla `deleted` esta VACIA.

    Con un INNER JOIN contra `deleted`, la comprobacion dejo de ejecutarse en las altas. Y
    trgClientesIns no comprueba NIF duplicados (solo bloquea el alta desde versiones viejas de
    Nesto). Resultado: desde ese momento se podia CREAR un cliente con un NIF que ya tenia otro,
    cosa que antes estaba bloqueada.

    Antes del cambio funcionaba porque el bloque va dentro de `if update([CIF/NIF])`, que en T-SQL
    es cierto tambien en los INSERT. Al cambiar la condicion interna se perdio esa cobertura.

    EL ARREGLO
    ----------
    INNER JOIN pasa a LEFT JOIN, y la condicion pasa a ser:

        no hay fila en `deleted`  (es un ALTA: se comprueba siempre)
        O el NIF ha cambiado      (es una MODIFICACION: solo si cambia)

    Con eso quedan cubiertos los dos casos y se conserva lo que se gano: modificar la direccion o
    un CCC de un cliente con NIF duplicado sigue funcionando.

    LO QUE NO HACE ESTE SCRIPT
    --------------------------
    No arregla los datos. Los 26 clientes que comparten NIF y los 79 con mas de un contacto
    principal siguen ahi: esto solo evita que se creen mas por esta via. Limpiar los que hay es una
    tarea de negocio (decidir cual sobrevive y que pasa con sus pedidos y facturas), no de un
    script.

    COMO ESTA HECHO
    ---------------
    Igual que el anterior: lee la definicion de sys.sql_modules, comprueba que el texto aparece
    EXACTAMENTE UNA VEZ, sustituye, cambia CREATE por ALTER y aplica en transaccion. Si no encuentra
    el texto, aborta sin tocar nada.

    COMO SE EJECUTA
    ---------------
    DDL sobre un disparador de produccion: desde SSMS con `sa`.
*/

USE NV;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @definicion nvarchar(max);
DECLARE @viejo      nvarchar(max);
DECLARE @nuevo      nvarchar(max);
DECLARE @apariciones int;
DECLARE @crlf       nchar(2) = CHAR(13) + CHAR(10);

SELECT @definicion = m.definition
FROM sys.sql_modules m
JOIN sys.triggers t ON t.object_id = m.object_id
WHERE t.name = 'trgClientesUpd';

IF @definicion IS NULL
BEGIN
    RAISERROR('No se encuentra el disparador trgClientesUpd. Se aborta.', 16, 1);
    RETURN;
END

SET @viejo =
      N'	if exists (' + @crlf
    + N'			select 1' + @crlf
    + N'			from inserted as i' + @crlf
    + N'			inner join deleted as d' + @crlf
    + N'				on  d.empresa      = i.empresa' + @crlf
    + N'				and d.[nº cliente] = i.[nº cliente]' + @crlf
    + N'				and d.contacto     = i.contacto' + @crlf
    + N'			where isnull(i.[CIF/NIF], '''') <> isnull(d.[CIF/NIF], '''')' + @crlf
    + N'			  and exists (select 1 from clientes c';

-- 09/09/26: se ejecuto dos veces y la segunda abortaba con "aparece 0 veces", que parecia un
-- fallo cuando en realidad el cambio YA estaba aplicado. Si el disparador ya lleva el LEFT JOIN,
-- se dice y se sale sin error.
IF CHARINDEX(N'where (d.empresa is null', @definicion) > 0
BEGIN
    PRINT 'trgClientesUpd: el cambio YA estaba aplicado (la comprobacion de NIF cubre las altas). No se hace nada.';
    RETURN;
END

SET @apariciones = (DATALENGTH(@definicion) - DATALENGTH(REPLACE(@definicion, @viejo, N''))) / DATALENGTH(@viejo);

IF @apariciones <> 1
BEGIN
    RAISERROR('El texto a sustituir aparece %d veces (se esperaba 1). Se aborta sin tocar nada.', 16, 1, @apariciones);
    RETURN;
END

SET @nuevo =
      N'	if exists (' + @crlf
    + N'			select 1' + @crlf
    + N'			from inserted as i' + @crlf
    + N'			left join deleted as d' + @crlf
    + N'				on  d.empresa      = i.empresa' + @crlf
    + N'				and d.[nº cliente] = i.[nº cliente]' + @crlf
    + N'				and d.contacto     = i.contacto' + @crlf
    + N'			-- LEFT y no INNER: el disparador tambien es FOR INSERT, y en un alta `deleted`' + @crlf
    + N'			-- esta vacia. Con INNER la comprobacion no corria en las altas y se podia crear' + @crlf
    + N'			-- un cliente con el NIF de otro (agujero del 08/09/26, corregido el mismo dia).' + @crlf
    + N'			where (d.empresa is null                                          -- es un ALTA' + @crlf
    + N'			       or isnull(i.[CIF/NIF], '''') <> isnull(d.[CIF/NIF], ''''))     -- o el NIF cambia' + @crlf
    + N'			  and exists (select 1 from clientes c';

SET @definicion = REPLACE(@definicion, @viejo, @nuevo);

IF CHARINDEX(N'CREATE TRIGGER [dbo].[trgClientesUpd]', @definicion) <> 1
BEGIN
    RAISERROR('La definicion no empieza por CREATE TRIGGER [dbo].[trgClientesUpd]. Se aborta.', 16, 1);
    RETURN;
END

SET @definicion = STUFF(@definicion, 1, LEN(N'CREATE TRIGGER [dbo].[trgClientesUpd]'),
                        N'ALTER TRIGGER [dbo].[trgClientesUpd]');

BEGIN TRANSACTION;

EXEC sp_executesql @definicion;

DECLARE @resultado nvarchar(max);
SELECT @resultado = m.definition
FROM sys.sql_modules m
JOIN sys.triggers t ON t.object_id = m.object_id
WHERE t.name = 'trgClientesUpd';

IF CHARINDEX(N'where (d.empresa is null', @resultado) = 0
   OR CHARINDEX(@viejo, @resultado) <> 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('El disparador no ha quedado como se esperaba: ROLLBACK.', 16, 1);
    RETURN;
END

COMMIT TRANSACTION;

PRINT 'trgClientesUpd: la comprobacion de CIF/NIF duplicado vuelve a cubrir las ALTAS.';
GO

/*
    QUE HAY QUE COMPROBAR DESPUES (a mano)
    --------------------------------------
    1. CREAR un cliente con el NIF de otro que este de alta -> tiene que dar
       "Ya existe un cliente con ese CIF/NIF". Esto es lo que se estaba escapando.
    2. MODIFICAR (p. ej. anadir un CCC) uno de los 26 clientes con NIF duplicado -> tiene que
       GUARDAR. Es lo que se arreglo esta manana y no se puede haber perdido.
    3. Cambiar el NIF de un cliente por el de otro de alta -> tiene que dar el error.
*/

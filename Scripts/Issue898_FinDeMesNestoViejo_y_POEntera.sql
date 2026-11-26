/*
    NestoAPI#898 (Carlos 26/11/26, URGENTE): «capar» el fin de mes de Nesto viejo y que una P.O. no se pueda facturar a medias.

    1) dbo.prdAgruparAlbaranesVta, llamado desde Nesto viejo (APP_NAME() = 'Nesto'): error y no agrupa nada. Era el primer paso
       del cierre de mes de Nesto viejo, que después reunía todos los contactos en un pedido sin mirar la P.O. (NV2616046).
       El fin de mes se hace en Nesto (POST api/FacturacionRutas/FinDeMes, en uso desde el 30/10/26). La API no usa este
       procedimiento para eso y no le afecta. OJO: también corta en Nesto viejo el «agrupar albaranes» de UN cliente.
       Volver atrás sin tocar el procedimiento: UPDATE ParametrosUsuario SET Valor = 'Sombra' WHERE Empresa = '1'
       AND Clave = 'NestoViejo_FinDeMes_Modo' AND Usuario = '(defecto)'. Dejar a un usuario: fila suya 'NestoViejo_FinDeMes' = '1'.

    2) dbo.prdCrearFacturaVta (por él facturan Nesto, Nesto viejo y el cierre de mes): el pedido con P.O. de un cliente con
       P.O. obligatoria (dbo.ClientesSuPedidoObligatorio; hoy solo el 27593) no se factura si le queda algo sin entregar o
       si hay otros pedidos de esa misma P.O. sin facturar. Apagar: fila «(defecto)» 'Facturacion.POSoloEntera' = '0'.

    Cómo lo hace: lee la definición que hay AHORA en el servidor, comprueba que tiene la cabecera y el punto de inserción
    esperados (una sola vez) y mete el bloque; si algo no cuadra, no cambia nada y lo dice. Se puede lanzar más de una vez
    (marca «NestoAPI#898-…» dentro del procedimiento). Lanzar como sa en la BD NV. Los dos procedimientos están creados con
    ANSI_NULLS y QUOTED_IDENTIFIER a ON (comprobado el 26/11/26): el script los mantiene.
*/
USE NV;
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 0) Interruptores (fila «(defecto)»)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_FinDeMes_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_FinDeMes_Modo', '(defecto)', 'Bloqueo', 'NestoAPI#898', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_FinDeMes' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_FinDeMes', '(defecto)', '0', 'NestoAPI#898', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Facturacion.POSoloEntera' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Facturacion.POSoloEntera', '(defecto)', '1', 'NestoAPI#898', GETDATE());
GO

-- 1) El fin de mes (agrupar albaranes) ya no se hace desde Nesto viejo
DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdAgruparAlbaranesVta'));
DECLARE @cab nvarchar(200) = N'CREATE PROCEDURE [dbo].[prdAgruparAlbaranesVta]';
DECLARE @ancla nvarchar(200) = N'-- Comprobación de parámetros';
DECLARE @bloque nvarchar(max) = N'-- NestoAPI#898-FinDeMesNestoViejo (Carlos 26/11/26): agrupar albaranes ya no se hace desde Nesto viejo. Después de este
-- procedimiento, Nesto viejo reunía todos los contactos del cliente en un pedido nuevo sin mirar la P.O. y con contacto 0
-- (así salió la NV2616046 del 27593, con tres P.O. mezcladas). El fin de mes se hace en Nesto, por la API.
-- Interruptores (ParametrosUsuario, empresa 1): ''NestoViejo_FinDeMes_Modo'' de «(defecto)» = Bloqueo | Sombra, y
-- ''NestoViejo_FinDeMes'' = 1 en la fila de un usuario para dejarle seguir.
if dbo.fnEsNestoViejo() = 1 begin
	declare @modo898 varchar(10), @permitido898 bit
	set @modo898 = dbo.fnNestoViejoModo(''FinDeMes'')
	set @permitido898 = dbo.fnNestoViejoPermitido(''FinDeMes'')
	if @modo898 = ''Bloqueo'' and @permitido898 = 0 begin
		raiserror (''Agrupar albaranes y el cierre de fin de mes ya no se hacen desde aquí: hágalo en Nesto (Facturar rutas, «Fin de mes»), que respeta la P.O. y la tienda de cada pedido. No se ha agrupado nada.'', 11, 1)
		return -1
	end
	insert into dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
	values (''FinDeMes'', case when @modo898 = ''Bloqueo'' then ''Permitido'' else ''Sombra'' end, ''CabPedidoVta'',
		left(rtrim(@Empresa) + ''/'' + rtrim(@NumCliente) + ''/'' + rtrim(@Contacto), 100),
		''Agrupar albaranes hasta el '' + convert(varchar(10), @FechaAlbarán, 103) + '', periodo '' + rtrim(@PeriodoFacturación))
end

';
IF @def IS NULL
    RAISERROR('No existe dbo.prdAgruparAlbaranesVta o no se puede leer su definición. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(N'NestoAPI#898-FinDeMesNestoViejo', @def) > 0
    PRINT 'dbo.prdAgruparAlbaranesVta: ya estaba aplicado (NestoAPI#898-FinDeMesNestoViejo). No se toca.';
ELSE IF CHARINDEX(@cab, @def) = 0 OR (LEN(@def) - LEN(REPLACE(@def, @ancla, N''))) / LEN(@ancla) <> 1
    RAISERROR('dbo.prdAgruparAlbaranesVta no tiene la cabecera o el punto de inserción esperados (ha cambiado desde el 26/11/26). No se ha cambiado nada: avise.', 16, 1);
ELSE
BEGIN
    SET @def = STUFF(@def, CHARINDEX(@cab, @def), LEN(@cab), REPLACE(@cab, N'CREATE', N'ALTER'));
    SET @def = STUFF(@def, CHARINDEX(@ancla, @def), 0, @bloque);
    EXEC sys.sp_executesql @def;
    PRINT 'dbo.prdAgruparAlbaranesVta: aplicado (NestoAPI#898-FinDeMesNestoViejo).';
END
GO

-- 2) Una P.O. se factura entera o no se factura
DECLARE @def nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID('dbo.prdCrearFacturaVta'));
DECLARE @cab nvarchar(200) = N'CREATE   PROCEDURE [dbo].[prdCrearFacturaVta]';
DECLARE @ancla nvarchar(200) = N'-- Comprobamos que no haya lineas con el visto bueno a false';
DECLARE @bloque nvarchar(max) = N'-- NestoAPI#898-POEntera (Carlos 26/11/26): una P.O. va en UNA sola factura que cuadre con el pedido del cliente. El pedido de un
-- cliente con P.O. obligatoria (ClientesSuPedidoObligatorio) no se factura mientras le quede algo sin entregar, ni mientras
-- haya otros pedidos de esa misma P.O. sin facturar (se agrupan antes por P.O.). O se entrega todo, o se quita del pedido lo
-- que no se va a entregar, o no se factura nada. Vale para Nesto, Nesto viejo y el cierre de mes: todo factura por aquí.
-- Interruptor: ParametrosUsuario empresa 1 «(defecto)» ''Facturacion.POSoloEntera'' (sin fila o distinto de 0 = encendido).
if isnull((select top 1 rtrim(ltrim(Valor)) from ParametrosUsuario where Empresa = ''1'' and Clave = ''Facturacion.POSoloEntera'' and Usuario = ''(defecto)''), ''1'') <> ''0'' begin
	declare @emp898 char(3), @po898 varchar(100), @cliente898 char(10), @otro898 int
	set @emp898 = cast(@Empresa as varchar(3))
	select @po898 = rtrim(ltrim(c.SuPedido)), @cliente898 = c.[Nº Cliente] from CabPedidoVta c where c.Empresa = @emp898 and c.Número = @Pedido
	if isnull(@po898, '''') <> '''' and exists (select 1 from ClientesSuPedidoObligatorio o where o.Empresa = @emp898 and o.Cliente = @cliente898) begin
		if exists (select 1 from LinPedidoVta l where l.Empresa = @emp898 and l.Número = @Pedido and l.Estado between -1 and 1 and l.Cantidad <> 0) begin
			raiserror(''El pedido %d es de la P.O. %s y no está todo entregado: una P.O. va en una sola factura. Entregue lo que falta, o quítelo del pedido, y vuelva a facturar. No se ha facturado nada.'', 11, 1, @Pedido, @po898)
			return -2 -- sale del procedimiento con error
		end
		select top 1 @otro898 = c2.Número
		from CabPedidoVta c2 inner join LinPedidoVta l2 on l2.Empresa = c2.Empresa and l2.Número = c2.Número
		where c2.Empresa = @emp898 and c2.[Nº Cliente] = @cliente898 and c2.Número <> @Pedido and rtrim(ltrim(c2.SuPedido)) = @po898
			and l2.Estado between -1 and 2 and l2.Cantidad <> 0
		order by c2.Número
		if @otro898 is not null begin
			raiserror(''La P.O. %s tiene más pedidos sin facturar (el %d, por ejemplo): una P.O. va en una sola factura. Agrupe antes los pedidos de esa P.O. (en Nesto, «Agrupar por P.O.») cuando estén todos entregados. No se ha facturado nada.'', 11, 1, @po898, @otro898)
			return -2 -- sale del procedimiento con error
		end
	end
end

';
IF @def IS NULL
    RAISERROR('No existe dbo.prdCrearFacturaVta o no se puede leer su definición. No se ha cambiado nada.', 16, 1);
ELSE IF CHARINDEX(N'NestoAPI#898-POEntera', @def) > 0
    PRINT 'dbo.prdCrearFacturaVta: ya estaba aplicado (NestoAPI#898-POEntera). No se toca.';
ELSE IF CHARINDEX(@cab, @def) = 0 OR (LEN(@def) - LEN(REPLACE(@def, @ancla, N''))) / LEN(@ancla) <> 1
    RAISERROR('dbo.prdCrearFacturaVta no tiene la cabecera o el punto de inserción esperados (ha cambiado desde el 26/11/26). No se ha cambiado nada: avise.', 16, 1);
ELSE
BEGIN
    SET @def = STUFF(@def, CHARINDEX(@cab, @def), LEN(@cab), REPLACE(@cab, N'CREATE', N'ALTER'));
    SET @def = STUFF(@def, CHARINDEX(@ancla, @def), 0, @bloque);
    EXEC sys.sp_executesql @def;
    PRINT 'dbo.prdCrearFacturaVta: aplicado (NestoAPI#898-POEntera).';
END
GO

-- Comprobación
SELECT OBJECT_NAME(object_id) AS Procedimiento, LEN(definition) AS Largo, uses_ansi_nulls, uses_quoted_identifier,
       CASE WHEN definition LIKE N'%NestoAPI#898-%' THEN 'aplicado' ELSE 'SIN APLICAR' END AS Estado
FROM sys.sql_modules WHERE object_id IN (OBJECT_ID('dbo.prdAgruparAlbaranesVta'), OBJECT_ID('dbo.prdCrearFacturaVta'));
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave IN ('NestoViejo_FinDeMes_Modo', 'NestoViejo_FinDeMes', 'Facturacion.POSoloEntera');
GO

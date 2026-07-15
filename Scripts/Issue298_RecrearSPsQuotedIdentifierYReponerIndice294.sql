-- Issue #298: recrear con QUOTED_IDENTIFIER ON los SPs que escriben en SeguimientoCliente
-- y reponer el índice anti-duplicados de la Issue #294.
--
-- CONTEXTO (15/07/26): el índice filtrado + columna calculada de #294 rompieron la facturación
-- (error 1934 en prdCrearFacturaVta) y se revirtieron (Issue294_ROLLBACK). La causa: SQL Server
-- exige QUOTED_IDENTIFIER ON (y ANSI_NULLS ON) —estampados en el módulo AL CREARLO— para hacer
-- DML sobre tablas con índices filtrados o columnas calculadas indexadas.
--
-- ANÁLISIS (con VIEW DEFINITION, 15/07/26): de los 15 módulos con QI OFF que mencionan
-- SeguimientoCliente, solo TRES le hacen DML de verdad (el resto son informes que leen o
-- escriben en tablas temporales #, y prdCrearClavesEmpresa solo inserta parámetros cuyo texto
-- la menciona):
--   1. prdComprobarRetenidosFacturaVta (QI=0, AN=1) - 2 INSERT. Es el que rompió la facturación:
--      lo llama prdCrearFacturaVta (línea ~902). prdCrearFacturaVta NO necesita recrearse:
--      el setting que manda en cada sentencia es el del módulo que la contiene.
--   2. prdModificarEfectoCliente (QI=0, AN=1) - 1 INSERT (rehusados RHS).
--   3. prdTransferirRapport (QI=0, AN=0) - 1 INSERT. OJO: también tenía ANSI_NULLS OFF, que
--      igualmente impide el DML con índice filtrado; su cuerpo no compara con '= NULL', así
--      que es seguro pasarlo a ON.
-- Los cuerpos de abajo son copia EXACTA de sys.sql_modules en prod (15/07/26), solo se cambia
-- CREATE por ALTER (conserva permisos). Ninguno usa comillas dobles como delimitador de cadenas.
--
-- ⚠️ EJECUTAR CON LOGIN ADMIN, en SSMS (o sqlcmd con flag -I, NUNCA sin él: sqlcmd conecta por
--    defecto con QUOTED_IDENTIFIER OFF y volvería a estampar mal los SPs).
-- ⚠️ AJUSTAR LA FECHA DEL FILTRO de la Parte 4 antes de ejecutar: día SIGUIENTE a la ejecución.
--
-- BD: NV (NestoConnection). ALTER PROCEDURE conserva permisos: no necesita GRANTs.

------------------------------------------------------------------------------------------------
-- PARTE 1: prdComprobarRetenidosFacturaVta (el que rompía la facturación)
------------------------------------------------------------------------------------------------
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE prdComprobarRetenidosFacturaVta  @Empresa as char(3),@NumFactura as char(10)  AS
-- David Sanchez... 12/08/04
-- procedimiento que me actualiza los retenidos de los clientes que tengan como condicion esperar a un abono.
-- lo comprueba con la factura que le pasamos por parametro y hace un apunte en el seguimiento del cliente
/*
declare @Empresa as char(3)
declare @NumFactura as char(10)
set @empresa='1'
set @NumFactura='NV0206220'
*/
-- antes de nada, si la factura no es un abono nos salimos
if (select count(*) from linpedidovta where empresa=@empresa and [nº factura]=@numfactura and cantidad<0)=0 begin
	return 1
end
-- buscamos el cliente y el contacto de la factura
declare @Cliente as char(10)
declare @Contacto as char(3)
select @cliente=[nº cliente],@contacto=contacto from cabfacturavta where
	empresa=@empresa and número=@numfactura
if @@Error!=0 begin
	raiserror('No se pudo determinar el cliente de la factura',16,1)
	return -1
end
-- buscamos si ese cliente tiene algo en estado retenido
-- y que este en la tabla retenidos
-- nos lo vamos recorriendo linea a linea, si el producto es nulo,
-- quitamos el retenido, si no lo fuera buscamos si en la factura hay un abono de este producto
-- y si no lo hay no lo quitamos
declare @NumOrden as int
declare @Producto as char(10)
declare crsRetenido cursor local dynamic optimistic for select [nºorden],producto from retenidos as r inner join extractocliente as e
							on r.[nºorden]=e.[nº orden]
							 where r.tipo=1 and e.numero=@cliente and e.contacto=@contacto and r.fecha is null and e.estado in(select numero from estadosextracto where bloquearliquidacion=1)
if @@Error!=0 begin
	raiserror('No se pudo abrir el cursor',16,1)
	return -1
end
open crsRetenido --abrimos el cursor
if @@Error!=0 begin
	raiserror('No se pudo abrir el cursor',16,1)
	return -1
end
-- iniciamos la transacion
begin transaction
fetch next from  crsRetenido into @NumOrden,@Producto
	while @@fetch_status =0 begin
		-- si el producto es null lo borramos
		if @producto is null begin
			-- modificamos el extracto
			update extractocliente set estado=null where [nº orden]=@numorden
			if @@Error!=0 begin
				raiserror('No se pudo modificar el extracto',16,1)
				rollback transaction
				return -1
			end
			-- insertamos en seguimiento cliente
			insert into seguimientocliente (empresa,número,tipo,contacto,fecha,comentarios,usuario,NumOrdenExtracto)
		            		values (@empresa,@cliente,'T',@Contacto,getdate(),'Quito estado retenido por abono',suser_sname(),@numorden)
			if @@Error!=0 begin
				raiserror('No se pudo modificar el extracto',16,1)
				rollback transaction
				return -1
			end
			-- borramos de retenidos
			delete retenidos where tipo=1 and [nºorden]=@numorden
			if @@Error!=0 begin
				raiserror('No se pudo modificar el extracto',16,1)
				rollback transaction
				return -1
			end
		end else begin
			-- si el producto no es nulo tenemos que ver si hay la factura tiene una linea abonada de este producto
			if (select count(*) from linpedidovta where empresa=@empresa and [nº factura]=@numfactura and producto=@producto and cantidad<0)>0 begin
			if @@Error!=0 begin
				raiserror('No se pudo modificar el extracto',16,1)
				rollback transaction
				return -1
			end
				-- modificamos el extracto
				update extractocliente set estado=null where [nº orden]=@numorden
				if @@Error!=0 begin
					raiserror('No se pudo modificar el extracto',16,1)
					rollback transaction
					return -1
				end
				-- insertamos en seguimiento cliente
				insert into seguimientocliente (empresa,número,tipo,contacto,fecha,comentarios,usuario,NumOrdenExtracto)
		            		values (@empresa,@cliente,'T',@Contacto,getdate(),'Quito estado retenido por abono del producto '+ltrim(rtrim(@producto)),suser_sname(),@numorden)
				if @@Error!=0 begin
					raiserror('No se pudo modificar el extracto',16,1)
					rollback transaction
					return -1
				end
				-- borramos de retenidos
				delete retenidos where tipo=1 and [nºorden]=@numorden
				if @@Error!=0 begin
					raiserror('No se pudo modificar el extracto',16,1)
					rollback transaction
					return -1
				end
			end
		end
		fetch next from  crsRetenido into @NumOrden,@Producto
	end
close crsRetenido -- cerramos el cursor
				if @@Error!=0 begin
					raiserror('No se pudo modificar el extracto',16,1)
					rollback transaction
					return -1
				end
deallocate crsRetenido -- esto es como el set x =nothing
				if @@Error!=0 begin
					raiserror('No se pudo modificar el extracto',16,1)
					rollback transaction
					return -1
				end
-- validamos la transacion
commit transaction
-- devolvemos un positivo
return 1

GO

------------------------------------------------------------------------------------------------
-- PARTE 2: prdModificarEfectoCliente
------------------------------------------------------------------------------------------------
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE [dbo].[prdModificarEfectoCliente] @NumOrden int, @FechaVto datetime, @CCC char(3), @Ruta as char(3),@Estado as char(3)  output,@Concepto as char(50)  AS
if @CCC = ''
	set @CCC = NULL
if @Ruta=''
	set @Ruta = NULL
if ltrim(rtrim(@Estado))=''
	set @Estado=null
if ltrim(rtrim(@Concepto))=''
	set @Concepto=null
-- David Sanchez Lopez-- 22/12/03
-- si el ccc y la ruta es nulo tendremos que poner el estado retenido
-- David Sanchez Lopez... 19/01/04
-- Ponemos para que me devuelva un 2 si se ha cambiado el estado a retenido por haber cambiado la ruta
-- si es asi hay que mandar un correo desde visual
declare @CambioEstado as bit
set @CambioEstado=0
-- David Sanchez  29/07/04
-- Si la forma de pago es talon, ya esta controlado por lo que no cmabiamos el estado
declare @FormaPagoTalon as char(3)
declare @FormaPago as char(3)
declare @Empresa as char(3)
select @Empresa=empresa,@FormaPago=ltrim(rtrim(FormaPago)) from extractocliente where [nº orden]=@NumOrden
if @@Error!=0 begin
	raiserror('No se ha podido modificar el efecto',16,1)
	return(-1)
end
set @FormaPagoTalon=(select ltrim(rtrim(FormaPagoTalón)) from empresas where numero=@empresa)
if @FormaPagoTalon!=@FormaPago begin
	if @ccc is null and @Ruta is null begin

		set @Estado=(select top 1 número from estadosextracto where empresa=@empresa and bloquearLiquidacion=1)
		set @CambioEstado=1
	end
end

-- Carlos 15/06/15: si la forma de pago es RHS y está hecho desde Nesto 2011 creamos el Retenido y el Seguimiento
if (@Estado='RHS') and ((select NºOrden from Retenidos where NºOrden=@NumOrden) is null) begin
	insert into retenidos (tipo,[nºorden]) values (1,@NumOrden)

	declare @cliente as char(15)
	declare @contacto as char(10)
	select @cliente = Número, @contacto = Contacto from ExtractoCliente where [Nº Orden] = @NumOrden

	insert into seguimientocliente (empresa,número,tipo,contacto,fecha,comentarios,usuario,NumOrdenExtracto)
	values (@Empresa,@cliente,'T',@contacto,GETDATE(),'Cambió el estado del extracto de cliente por Rehusado.',SYSTEM_USER,@NumOrden)
end

update extractocliente set fechavto=@fechavto,ccc=@ccc,ruta=@Ruta,estado=@estado,concepto=@concepto where [nº orden]=@numorden
if @estado is null set @estado=''
if @@error = 0
	if @CambioEstado=0 begin -- si no ha cambiado el estado devolvemos un uno
		return(1)
	end else begin
		return (2) -- devolvemos un dos para saber en visual si se ha puesto el estado retenido
	end
else begin
	raiserror('No se ha podido modificar el efecto',16,1)
	return(-1)
end

GO

------------------------------------------------------------------------------------------------
-- PARTE 3: prdTransferirRapport (tenía QI OFF y ANSI_NULLS OFF; se estampan ambos en ON.
-- Su cuerpo no compara con '= NULL', así que el cambio de ANSI_NULLS no altera la semántica)
------------------------------------------------------------------------------------------------
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO
ALTER PROCEDURE prdTransferirRapport @Empresa as char(3),@FechaDesde as datetime,@FechaHasta as datetime  AS

/*

declare @empresa as char(3)
declare @Fechadesde as datetime
declare @FechaHasta as datetime
declare @Vendedor as char(3)
set @FechaDesde='01/09/08'
set @FechaHasta='30/09/08'


--set @Vendedor= 'DO' --'VI' 'AL'  'JA' 'JC'  'JM'  'OS'  'DO'
set @Empresa='1'

*/

set @FechaDesde = str(day(@FechaDesde))+'/'+str(month(@FechaDesde))+'/'+str(year(@FechaDesde))+' 00:00:00'
set @FechaHasta = str(day(@FechaHasta))+'/'+str(month(@FechaHasta))+'/'+str(year(@FechaHasta))+' 23:59:59'

insert into seguimientocliente (empresa,numero,contacto,fecha,tipo,vendedor,pedido,clientenuevo,aviso,aparatos,datosbanco,gestionaparatos,primeravisita,
comentarios ,estado,numordenextracto)
select @empresa,numero,contacto,fecha,tipo,vendedor,pedido,clientenuevo,aviso,aparatos,datosbanco,gestionaparatos,primeravisita,
cast(comentarios as char),estado,numordenextracto
 from seguimientoclientepuente where  fecha between @FechaDesde and @FechaHasta
group by numero,contacto,fecha,tipo,vendedor,pedido,clientenuevo,aviso,aparatos,datosbanco,gestionaparatos,primeravisita,
cast(comentarios as char),estado,numordenextracto
GO

-- VERIFICACIÓN partes 1-3 (los tres deben tener uses_quoted_identifier = 1 y uses_ansi_nulls = 1):
SELECT o.name, m.uses_quoted_identifier, m.uses_ansi_nulls
FROM sys.sql_modules m JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.name IN ('prdComprobarRetenidosFacturaVta','prdModificarEfectoCliente','prdTransferirRapport');
GO

------------------------------------------------------------------------------------------------
-- PARTE 4: reponer columna + índice de la Issue #294 (idéntico a Issue294_IndiceUnicoSeguimientoDiario.sql)
-- ⚠️ AJUSTAR LA FECHA DEL FILTRO: día SIGUIENTE a la ejecución de este script.
------------------------------------------------------------------------------------------------
SET LOCK_TIMEOUT 15000;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'FechaDia')
BEGIN
    ALTER TABLE dbo.SeguimientoCliente ADD FechaDia AS CONVERT(date, Fecha) PERSISTED;
END
SET LOCK_TIMEOUT -1;
GO

SET LOCK_TIMEOUT 15000;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'UQ_SeguimientoCliente_UnoPorClienteUsuarioDia')
BEGIN
    CREATE UNIQUE NONCLUSTERED INDEX UQ_SeguimientoCliente_UnoPorClienteUsuarioDia
        ON dbo.SeguimientoCliente ([Número], Contacto, Usuario, FechaDia)
        WHERE Estado <> 2
          AND [Número] IS NOT NULL
          AND Usuario IS NOT NULL
          AND Fecha >= '20260716';  -- ⚠️ AJUSTAR: día SIGUIENTE a la ejecución
END
SET LOCK_TIMEOUT -1;
GO

-- VERIFICACIÓN parte 4 (1 fila con has_filter = 1 + la columna FechaDia con is_persisted = 1):
SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'UQ_SeguimientoCliente_UnoPorClienteUsuarioDia';
SELECT name, is_persisted FROM sys.computed_columns WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente');
GO

-- PRUEBA FINAL: crear una factura real (o de un pedido de prueba) para verificar que
-- prdCrearFacturaVta -> prdComprobarRetenidosFacturaVta ya no da error 1934.

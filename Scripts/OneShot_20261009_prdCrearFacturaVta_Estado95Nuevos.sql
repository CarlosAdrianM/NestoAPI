-- 09/10/26 (Carlos): los clientes NUEVOS cuya única compra es un pedido de Amazon pasan solos al estado 95
-- «Una sola compra, por Amazon» al facturarla. El 08/10 se pasaron a mano los que ya había
-- (OneShot_20261008_ClientesAmazonUnaCompraEstado95.sql) y este procedimiento ya devolvía el 95 a 0 cuando compran algo
-- que no es de Amazon; faltaba la entrada (caso real: 42013/0, MPP, pedido 928163 del 08/10, se quedó en estado 9).
-- Regla (decisión de Carlos 09/10/26, «una sola compra»): al facturar, el cliente está en 0 o 9, TODAS las líneas de esta
-- factura son de Amazon (Forma Venta = 'STK') y no tiene ningún otro pedido facturado. Sin correo (no es una recuperación).
-- Un cliente con dos compras de Amazon se queda como esté.
--
-- Único cambio respecto a la versión en producción (comprobada el 09/10/26 contra OBJECT_DEFINITION, igual que
-- OneShot_20261008_prdCrearFacturaVta_Estado95.sql): el bloque «Carlos 09/10/26» tras la recuperación a estado 0.
-- Lanzar como sa (QUOTED_IDENTIFIER ON y ANSI_NULLS ON, como está). Repaso de los que han caído en el hueco desde el 08/10:
-- volver a lanzar OneShot_20261008_ClientesAmazonUnaCompraEstado95.sql (idempotente; COMMIT a mano). Vuelta atrás:
-- relanzar OneShot_20261008_prdCrearFacturaVta_Estado95.sql.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
/************************************************************************************************************************************************************************************
 * Crear Factura Venta																*
 * --------------------------------																*
 * Este procedimiento crea una cabecera de factura y a la vez la contabilidad y movimientos en el extracto del cliente correspondientes a la misma	*
 *																		*
 * Valores de retorno:																*
 * 0 : Correcto.																	*
 * -1: error en transacción																*
 * -2: error fuera de transacción															*
 * -3: descuadre																	*
 * Carlos Adrián Martínez (02/11/01)														*
 ************************************************************************************************************************************************************************************/
CREATE OR ALTER PROCEDURE [dbo].[prdCrearFacturaVta] @Empresa int, @Pedido int, @Fecha as datetime, @NumFactura as char(10) output, @Usuario varchar(30) = NULL AS

/*
if @empresa = '1' and system_user<> 'nuevavision\carlos' begin
	raiserror('Ahora no se puede, bloqueado por Carlos. Espera 5 minutos, por favor.',11,1)
	return -2 -- sale del procedimiento con error
end
*/

/*
if system_user!='NUEVAVISION\Carlos' begin
	raiserror('Ahora no se puede, bloqueado por Carlos. Espera un par de minutos, por favor',11,1)
	return -2 -- sale del procedimiento con error
end
*/


-- David Sanchez 11/09/03.. Es para poner aqui el pedido del error del buffer y no tener que ir hasta abajo.
declare @PedidoError as int
set @PedidoError =186014

-- Carlos 05/03/25: si no pasan el parámetro usuario ponemos el del sistema
if @Usuario is null OR CHARINDEX('$', SUSER_NAME()) = 0 begin
	set @Usuario = SYSTEM_USER
end


/*
-- Buscamos si hay productos que necesitan el numero de serie.
if (select count(l.producto) from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
	where  l.número = @Pedido and l.estado = 2  and l.empresa=@empresa
	and p.NecesitaNumSerie=1 and l.numserie is null)>=1 begin
	
	raiserror('Hay productos que necesitan el número de serie en las líneas.',11,1)
	return -2 -- sale del procedimiento con error
end
*/
-- David sanchez... 12-05-08 ... buscamos si hay productos que necesiten numero de serie y aunque lo
-- tengan puesto, lo tienen mal metido. 
/*
if (select count(l.producto) from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
	where  l.número = @Pedido and l.estado = 2  and l.empresa=@Empresa
	and p.NecesitaNumSerie=1)>=1 begin


	if (select count(l.producto)  from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
		left join numerosserie as n on (l.producto=n.producto or n.producto is null) 
			and (l.numserie=n.numserie or n.numserie is null)
		where  l.número = @Pedido and l.estado = 2  and l.empresa=@Empresa
			and p.NecesitaNumSerie=1 and n.numserie is null)>=1 begin

		raiserror('Hay productos que el número de serie no corresponde con ninguno.',11,1)
		return -2 -- sale del procedimiento con error
	end

end 
*/

-- David Sanchez 06/11/07... Miramos si el cliente esta en estado 5 y si esta en estado 5 no dejamos facturarlo
if (
select cli.estado from cabpedidovta as c inner join clientes as cli on c.empresa=cli.empresa and c.[nº cliente]=cli.[nº cliente]
	and c.contacto=cli.contacto where c.empresa=@empresa and c.numero=@Pedido)=5 begin
	raiserror('No se puede hacer una factura a un estado 5',11,1)
	return -2 -- sale del procedimiento con error
	
end

-- David Sanchez 28/07/04
-- No dejamos facturar por la forma de pago talon
declare @FormaPagoTalon as char(3)

set @FormaPagoTalon=(select [forma pago] from cabpedidovta where empresa=@empresa and número=@Pedido)
if @@Error!=0 begin
	raiserror('No se puede determinar la forma de pago',11,1)
	return -2 -- sale del procedimiento con error
end 
if ltrim(rtrim(@FormaPagoTalon))=(select ltrim(rtrim(formapagotalón)) from empresas where numero=@empresa) begin
	raiserror('No se puede crear una factura con forma de pago talón',11,1)
	return -2 -- sale del procedimiento con error
end


-- David 06/04/06
-- Tenemos que mirar si hay que modificar las fecha de la factura y la fecha del primer vencimiento
-- Si el campo FijarPrimerVto de la cabecera es 1 no haremos nada, pero si el campo es 0 tendremos 
-- que recalcular las fechas a dia de hoy.
declare @FijarVto as bit
declare @Cli as char(15)
declare @Cont as char(3)
declare @Plazos as char(15)
declare @PrimerVtoProd as datetime
declare @DevFijar as integer

select @fijarVto=fijarprimervto,@cli=[nº cliente],@cont=contacto,@plazos=plazospago from cabpedidovta where empresa=@empresa and número=@pedido
if @@Error!=0 begin
	raiserror('No se puede determinar los plazos de pago',11,1)
	return -2 -- sale del procedimiento con error
end 



if @FijarVto=0 begin -- si no hay que fijar el vencimiento tendremos que recalcular
	if @Empresa!='5'  and @Empresa!='6'   and @Empresa!='2' begin
	-- Llamamos al procedimiento
	set @fecha=getdate()
	set @fecha = str(day(@fecha))+'/'+str(month(@fecha))+'/'+str(year(@fecha))+' 00:00:00'
	exec @DevFijar=prdCalcularFechaPrimerVto @empresa,@Cli,@Cont,@Fecha,@Plazos,@PrimerVtoProd output
	if @DevFijar<0 begin
		raiserror('No se puede poner el nuevo plazo de pago',11,1)
		return -2 -- sale del procedimiento con error

	end

	-- tendremos que modificar  el campo del primer vencimiento del pedido
	update cabpedidovta set [primer vencimiento]=@primervtoprod where empresa=@empresa and numero=@Pedido
	if @@Error!=0 begin
		raiserror('No se puede poner el nuevo plazo de pago',11,1)
		return -2 -- sale del procedimiento con error
	end
	end 

end

declare @FechaEsVálida as int -- 02/10/02 Carlos: resultado de llamar al prdFechaEsVálida

exec @FechaEsVálida = prdFechaEsVálida @Empresa, @Fecha, 2, @Usuario -- Ventas


if @FechaEsVálida<>1 begin
	raiserror('Fecha no permitida',11,1)
	return -2 -- sale del procedimiento con error
end
/* Anulado el 04/09/02 por Carlos porque se le pasa la @Fecha como parámetro)
-- Coger fecha actual
declare @Fecha datetime
set @Fecha = getdate()
*/
-- Comprobar si hay líneas para contabilizar y Calcular Delegación y Forma Venta para cabeceras
declare @NumLineas tinyint,@DelegaciónCab char(3),@FormaVentaCab char(3)
set @NumLineas = (select count(distinct delegación) from linpedidovta where número = @Pedido and estado = 2)
if @NumLineas < 1 begin
	raiserror('No hay líneas para facturar',11,1)
	return -2 -- sale del procedimiento con error
end
-- Comprobamos que no haya lineas con el visto bueno a false
-- David Sanchez 17/02/03
if (select count(número) from linpedidovta where  número = @Pedido and estado = 2 and vtobueno=0 and empresa=@empresa) >0 begin
	raiserror('Hay lineas que no tienen el visto bueno dado.',11,1)
	return -2 -- sale del procedimiento con error
end
--- David Sanchez Lopez... 26/05/04... comprobamos que no haya ningun tipo de linea nulo
if (select count(número)  from linpedidovta where (ltrim(rtrim(tipolinea))='' or tipolinea is null) and número=@pedido and empresa=@empresa and estado=2)>0 begin
	raiserror('Hay líneas que no tiene determinado el tipo de linea.',11,1)
	return -2 -- sale del procedimiento con error
end 

/*
-- Carlos Adrián Martínez: 10/06/21. Comprobamos que no se facture nada sin picking
if exists (
	select * from LinPedidoVta where Empresa = @Empresa and Número = @Pedido and Estado = 2 and Almacén = 'ALG' and Cantidad > 0 and ISNULL(Picking, 0) = 0
) begin
	raiserror('Se están intentando facturar líneas sin picking',11,1)
	return -2 -- sale del procedimiento con error
end
*/

-- lo he puesto en la restriccion de la tabla
/*
-- David Sanchez Lopez 15/09/03
-- tenemos que comprobar que no hay lineas que el bruto no sea igual que el precio*cantidad
if (select count(número) from linpedidovta where  número = @Pedido and estado = 2  and empresa=@empresa and bruto<>precio*cantidad and tipolinea=1) >0 begin
	raiserror('Hay lineas que el bruto o el precio no coincide',11,1)
	return -2 -- sale del procedimiento con error
end
*/
-- Comprobamos que el cliente tenga dni en la ficha
-- pero solo si la empresa no es espejo
-- David Sanchez 24/07/03
if (select count(*) from empresas where [iva por defecto]=@empresa)=0 begin
declare @cif as varchar(20)
set @cif=(select [cif/nif] from clientes as c inner join cabpedidovta as ca
on c.empresa=ca.empresa and c.[nº cliente]=ca.[nº cliente] and c.contacto=ca.contacto
	where ca.empresa = @empresa and ca.número=@pedido)

if @cif is null begin
	raiserror('No se puede facturar. El cliente no tiene D.N.I. en la ficha',11,1)
	return -2 -- sale del procedimiento con error
end
end
-- Comprobamos que no hay lineas sin famlilia
-- David 03/07/03
if (select count(número) from linpedidovta where  número = @Pedido and estado = 2 and empresa=@empresa and tipolinea=1 and familia is null) >0 begin
	raiserror('Hay lineas que no tienen familia, no se puede facturar.',11,1)
	return -2 -- sale del procedimiento con error
end
-- Fin de la comprobacion
if @NumLineas = 1 begin
	select @DelegaciónCab = Delegación from LinPedidoVta where número = @Pedido and estado = 2
end else begin
	select @DelegaciónCab = DelegaciónVarios from empresas where número = @empresa
end
set @NumLineas = (select count(distinct [forma venta]) from linpedidovta where número = @Pedido and estado = 2)
if @NumLineas = 1 begin
	select @FormaVentaCab = [Forma Venta] from LinPedidoVta where número = @Pedido and estado = 2
end else begin
	select @FormaVentaCab = FormaventaVarios from empresas where número = @empresa
end
-- Calcular número de factura
-- David SAnchez 14/04/03... Le meto el contacto cobro
-- David sanchez 17/10/03 -- le meto el no comisiona
declare @UltNumFactura char(10),@NumCliente char(10),@Contacto char(3),@Serie char(3),@FormaPago char(3),@IVA char(3),@PlazosPago char(10),@LongContador tinyint,@Vendedor char(3),@PrimerVto datetime,@CCC char(3),@Ruta char(3),@EsHueco as bit, @Origen char(3),@ContactoCobro char(3),@NoComisiona decimal (5,4)
declare @Operador as char(3)
declare @MantenerJunto as bit 
select @Operador=operador,@Serie = serie,@NumCliente = [nº cliente],@Contacto = Contacto,@formapago = [forma pago],@IVA = IVA,@plazospago = plazospago,@Vendedor = vendedor,@PrimerVto = [Primer Vencimiento],@CCC=CCC,@Ruta=Ruta, @Origen = Origen,
	@contactocobro=contactocobro,@NoComisiona=nocomisiona, @MantenerJunto = MantenerJunto
from cabpedidovta 
where empresa = @Empresa and número = @pedido

if @serie is null begin
	raiserror('Debe especificar una serie',11,1)
	return -2
end else if @serie = 'CV' begin
	if exists (
		select *
		from LinPedidoVta
		where empresa = @Empresa and número = @pedido and Grupo <> 'CUR'
	) begin
		raiserror('No se puede facturar producto en la serie CV',11,1)
		return -2
	end
end else if @serie != 'CV' and @serie != 'RC' and @Empresa != '3' begin
	if exists (
		select *
		from LinPedidoVta
		where empresa = @Empresa and número = @pedido and Grupo = 'CUR'
	) begin
		raiserror('No se pueden facturar cursos si no es en la serie CV (o RC para rectificativas)',11,1)
		return -2
	end
end


-- Carlos 18/11/25: no se puede facturar desde Nesto viejo
if (SYSTEM_USER != 'NUEVAVISION\RDS2016$'  -- esta es la única condición que debe quedar cuando esté todo implementado
		and SYSTEM_USER != 'NUEVAVISION\Carlos'
		and @Serie != 'GB'
		/*
		and @Serie not in ('CV', 'GB')
		and exists (
			select * from linpedidovta where empresa = @empresa and Número = @Pedido and Almacén<>'AMZ' --Almacén<>'ALG' and 
		)
		*/
		) begin
	raiserror('No se puede facturar desde esta versión obsoleta de Nesto. Reintente desde una versión más moderna.',11,1)
	return -2 -- sale del procedimiento con error
end

-- David Sanchez 14/11/05... buscamos la cadena
-- David Sanchez...23/11/06... buscamos el estado del cliente
declare @Cadena as char(23)
declare @EstadoCliente as int
select @Cadena=cadena,@estadocliente=estado  from clientes where empresa='1' and [nº cliente]=@numcliente and contacto=@contacto
if @@Error!=0  begin
	raiserror('No se puede determinar la cadena',11,1)
	return -2
end 
-- David Sanchez 28/05/03--- Buscamos el estado del vendedor miramos a ver si esta anulado. Si fuera asi no le dejamos contabilizar
declare @EstadoVendedor as smallint
set @estadoVendedor=(select estado from vendedores where empresa=@empresa and número=@vendedor)
if @EstadoVendedor<0 begin
	raiserror('Esta intentando facturar a un vendedor anulado',11,1)
	return -2
end

if @EstadoVendedor=4 and exists (
		select *
		from LinPedidoVta
		where empresa = @Empresa and número = @pedido and Grupo <> 'CUR' and [Nº Cliente]<>'9063'
) begin
	raiserror('Esta intentando facturar a un vendedor que comisiona por grupo de producto',11,1)
	return -2
end


--David Sánchez 19/05/06.. tenemos que comprobar si la serie admite facturas posteriores o no
declare @NoCalcular as bit
set @NoCalcular= (select NoComprobarFacturaPosterior from series where empresa=@Empresa and numero=@serie)

set @NumFactura = null
select @NumFactura = Número from HuecosSerie where empresa = @Empresa and Serie = @Serie and Fecha = @Fecha order by [fecha modificación] desc
if (@NumFactura is null) or (@NumFactura='') begin -- si no hay huecos lo cogemos del contador de la serie
	set @EsHueco = 0
	if @NoCalcular=0 begin
		if @Fecha < (select max(fecha) from cabfacturavta where empresa = @Empresa and serie = @Serie) begin
			raiserror ('Existe una factura con fecha anterior y número posterior',11,1)
			return -2
		end
	end
	select @UltNumFactura = Contador from series where número =  @Serie and empresa = @Empresa
	select @NumFactura
	select @UltNumFactura
	set @LongContador = len(@ultnumfactura)
	set @UltNumFactura = @UltNumFactura + 1
	set @ultnumfactura = ltrim(str(@ultnumfactura))
	
	--print cast( len(@UltNumFactura) as char)  +' len ultnum'
	--print cast(@longcontador as char)+ ' contador'
	--print cast(@Ultnumfactura as char)
	
	while len(@UltNumFactura)<@LongContador

		set @UltNumFactura = '0'+ltrim(str(@UltNumFactura)) 

	
	/*
	if len(@UltnumFactura)=1 begin
		set @Ultnumfactura='0000'+@Ultnumfactura
	end else  if  len(@UltnumFactura)=2 begin	
		set @Ultnumfactura='000'+@Ultnumfactura
	end else  if  len(@UltnumFactura)=3 begin	
		set @Ultnumfactura='00'+@Ultnumfactura
	end else begin
		raiserror('Error en el numero',11,1)
		return -2
		
	end 

	print @NumFactura
	*/

	set @NumFactura = left(@Serie,len(@serie)) + @ultnumfactura


end else begin -- sí si que encuentra un hueco se borra como hueco
	set @EsHueco = 1
end

-- Calcular Cuentas de Descuentos
declare @CtaDtoCliente char(10),@CtaDtoProductoVta char(10),@CtaDescuentoVta char(10),@CtaDtoPPVta char(10)
	--Comentada por Carlos 22/08/02 porque ahora los descuentos de producto van en el GruposProducto @CtaDtoProductoVta. Linea original:
	--select @CtaDtoCliente = CtaDtoCliente,@CtaDtoProductoVta = CtaDtoProductoVta,@CtaDescuentoVta = CtaDescuentoVta,@CtaDtoPPVta = CtaDtoPPVta from empresas where número = @Empresa
select @CtaDtoCliente = CtaDtoCliente,@CtaDescuentoVta = CtaDescuentoVta,@CtaDtoPPVta = CtaDtoPPVta from empresas where número = @Empresa
-- Comprobar si la empresa es primaria o espejo
-- Carlos Adrian Martínez - 09/07/02
declare @EmpresaEspejo as char(3)
select @EmpresaEspejo = [IVA por defecto] from empresas where número=@Empresa
if (@IVA is null) and (@EmpresaEspejo is null) begin
	raiserror('Debe especificar un IVA',11,1)
	return -2
end


-- Carlos 27/03/25: si es mantener junto no dejamos facturar si no son todas las líneas.
if @MantenerJunto = 1 and exists (
	select *
	from LinPedidoVta
	where empresa = @Empresa and número = @pedido and Estado < 2
) begin
	raiserror('[WARNING] No se puede facturar porque tiene marcado el servir junto y hay líneas que no tienen el albarán creado',11,1)
	return -2
end

/*************************************
 * COMIENZA TRANSACCIÓN *
 **************************************/
begin transaction
if @EsHueco = 1 begin
	delete huecosserie where empresa = @Empresa and serie = @Serie and número = @NumFactura
	if @@error != 0 begin
		raiserror('No se ha podido borrar el hueco en series',11,1)
		rollback
		return(-1)
	end
end else begin
	update series set contador = @UltNumFactura where empresa = @Empresa and número = @Serie
	if @@error != 0 begin
		raiserror('No se ha podido actualizar en contador de facturas',11,1)
		rollback
		return(-1)
	end

end

-- David Sanchez 12-05-08  tenemos que borrar de la tabla de numeros de serie
-- las que facturemos
delete numerosserie  from linpedidovta as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
		left join numerosserie as n on (l.producto=n.producto or n.producto is null) 
			and (l.numserie=n.numserie or n.numserie is null)
		where  l.número = @Pedido and l.estado = 2  and l.empresa=@Empresa
			and p.NecesitaNumSerie=1 

	if @@error != 0 begin
		raiserror('No se ha podido borrar de numeros de serie',11,1)
		rollback
		return(-1)
	end


-- Crear cabecera de Factura
if @PrimerVto is null begin --> Carlos 04/11/02
	raiserror('No se ha especificado fecha del primer vencimiento',11,1)
	rollback
	return(-1)
end
if @PrimerVto<@Fecha -- Carlos 02/10/02
	set @PrimerVto=@Fecha

if isnull(@origen, '') = ''
	set @Origen = '1'

if isnull(@ContactoCobro, '') = ''
	set @ContactoCobro = @Contacto


-- David Sanchez Le meto el contacto cobro 14/4/03
insert into CabFacturaVta (empresa,número,fecha,[nº cliente],contacto,[forma pago],IVA,plazospago,vendedor,CCC,[Primer Vencimiento],Serie, Origen,contactocobro,NoComisiona,Operador,cadena,estadocliente, Usuario)
	 values (@empresa,@NumFactura,@fecha,@NumCliente,@Contacto,@formapago,@IVA,@plazospago,@vendedor,@CCC,@PrimerVto,@Serie, @Origen,@contactocobro,@NoComisiona,@Operador,@Cadena,@estadoCliente, @Usuario)
if @@error != 0 begin
	raiserror('No se ha podido crear la cabecera de factura',11,1)
	rollback
	return(-1)
end

-- Carlos Adrián Martínez: guardamos los vendedores por grupo de producto
exec prdActualizarComisionesPorGrupoProducto @empresa, @Pedido
if @@error != 0 begin
	raiserror('No se han podido actualizar los vendedores por grupo de producto',11,1)
	rollback
	return(-1)
end

-- Carlos Adrián Martínez: 20/06/24. Calculamos el vendedor real en base a las lineas
declare @vendedorReal as char(3)
;WITH VendedorCount AS (
    SELECT 
        Vendedor,
        SUM([Base Imponible]) AS TotalBaseImponible,
        COUNT(*) AS VendedorCount
    FROM VendedorLinPedidoVta v 
    INNER JOIN LinPedidoVta l ON v.Id = l.[Nº Orden]
    WHERE l.Número = @Pedido and l.Estado = 2
    GROUP BY 
        Vendedor
)
SELECT 
    @vendedorReal = COALESCE((
        SELECT TOP 1 Vendedor
        FROM VendedorCount
        ORDER BY 
            TotalBaseImponible DESC,
            VendedorCount DESC,
            Vendedor
    ), @vendedor); -- En caso de que no haya ninguna línea, pone el vendedor de cabecera, pero no debería ocurrir nunca

-- Carlos 10/04/26: ponemos la familia donde falte
update linpedidovta set familia='Cursos' where empresa=@empresa and número=@pedido and TipoLinea=2 and grupo='CUR' and (familia is null or ltrim(rtrim(familia))='')
update linpedidovta set familia='Genéricos' where empresa=@empresa and número=@pedido and TipoLinea=2 and grupo is not null and (familia is null or ltrim(rtrim(familia))='') and (Producto like '700%' or Producto like '752%')

-- Actualizar líneas pedido
-- David Sanchez... 13/07/06.. actualizamos todas menos las que el tipo de linea sea 2, para que no me tenga en cuenta estas a al hora de cojer la cuenta 700.
-- una vez que tenga la cuenta 700 actualizo las otras
update LinPedidoVta set [nº factura] = @NumFactura,[fecha factura] = @fecha,estado = 4 where número = @pedido and estado = 2 and empresa=@empresa and tipolinea!=2
if @@error != 0 begin
	raiserror('No se han podido actualizar las líneas de factura',11,1)
	rollback
	return(-1)
end
-- Actualizar Extracto producto: [CARLOS] 05/11/01
update ExtractoProducto set factura = @NumFactura,importe = [base imponible] from linpedidovta where extractoproducto.albarán = linpedidovta.[nº albarán] and 
	extractoproducto.linpedido = linpedidovta.[nº orden] and linpedidovta.[nº factura]=@NumFactura  --and extractoproducto.empresa = @empresa and linpedidovta.empresa = @empresa
if @@error != 0 begin
	raiserror('No se han podido actualizar las líneas de extracto producto',11,1)
	rollback
	return(-1)
end

/*
-- Crear bonificaciones [CARLOS] 28/11/02
declare @ImporteBonificaciónGenerada as money
declare @NumOrdenBonifGenerada as int
set @ImporteBonificaciónGenerada = isnull((select round(sum(BonificaciónGenerada),2) from (select sum(L.[base imponible]) * g.[%bonificación] as BonificaciónGenerada from linpedidovta as L inner join GruposProducto as G on l.empresa = g.empresa and l.grupo=g.número where L.empresa = @Empresa and [nº factura] = @NumFactura and L.GeneraBonificación=1 group by g.[%bonificación]) as Boni),0)
--- David Sanchez Lopez 20/05/03
-- Le paso el contacto de bonificacion en vez del contacto normal
declare @ContactoBonificacion as char(3)
set @contactoBonificacion=(select contactobonificacion from clientes where empresa=@empresa and [nº cliente]=@numcliente and contacto=@contacto)
if @ImporteBonificaciónGenerada != 0 begin
	--insert into Bonificaciones (Empresa, Aplicada, Número, Contacto, NºDocumento, Importe, ImportePdte, Fecha) values (@Empresa, 0, @NumCliente, @Contacto, @NumFactura, @ImporteBonificaciónGenerada, @ImporteBonificaciónGenerada, @Fecha)
	insert into Bonificaciones (Empresa, Aplicada, Número, Contacto, NºDocumento, Importe, ImportePdte, Fecha) values (@Empresa, 0, @NumCliente, @ContactoBonificacion, @NumFactura, @ImporteBonificaciónGenerada, @ImporteBonificaciónGenerada, @Fecha)
	if @@error != 0 begin
		raiserror('No se han podido insertar las bonificaciones',11,1)
		rollback
		return(-1)
	end
	set @NumOrdenBonifGenerada = @@identity
	-- si da error de buffer de busqueda poner el numero de pedido q da el error
	
	if @ImporteBonificaciónGenerada < 0 and @Pedido != @PedidoError begin -- Liquido la bonificación porque es negativa
		declare curBonif cursor local fast_forward for select nºorden, ImportePdte from bonificaciones where empresa = @Empresa and Número = @NumCliente and importePdte>0 and [nºorden]!= @NumOrdenBonifGenerada
		declare @NumOrdenBonif int
		declare @ImporteBonifNegativa as money
		declare @DevueltoLiquidar as smallint
		open curBonif
		fetch next from curBonif into @NumOrdenBonif, @ImporteBonifNegativa
		while @@fetch_status = 0 and @ImporteBonificaciónGenerada<0 begin
			
			exec  @devueltoliquidar= prdliquidarbonificaciones @Empresa, @NumOrdenBonif, @NumOrdenBonifGenerada
			if @@error != 0 begin
				raiserror('No se han podido liquidar las bonificaciones',11,1)
				close curBonif
				deallocate curBonif
				rollback
				return(-1)
			end else if @devueltoliquidar<0 begin
				raiserror('No se han podido liquidar las bonificaciones',11,1)
				close curBonif
				deallocate curBonif
				rollback
				return(-1)
			end
			set @ImporteBonificaciónGenerada = @ImporteBonificaciónGenerada + @ImporteBonifNegativa
			fetch next from curBonif into @NumOrdenBonif, @ImporteBonifNegativa
		end -- de la liquidación de bonificaciones
	
		if @ImporteBonificaciónGenerada < 0 begin
			raiserror('No se hay suficiente bonificación acumulada',1,1) 
			/* -- Esto habrá que descomentarlo cuando no se permita dejar la bonificación negativa
			close curBonif
			deallocate curBonif
			rollback
			return(-1) 
			*/
		*/
/*
		end	
		close curBonif
		deallocate curBonif
	end -- de liquidar porque la bonificación era negativa
         
end -- insertar bonificación
*/
-- Crear Contabilidad
declare @TotalCliente money,@BaseImponible money,@Delegación char(3),@FormaVenta char (3),@CtaVentas char(10),@CtaAbonosVta char(10),@NumAsiento int,@IVACliente char(3),@IVAProducto char(3),
	@ImporteBruto money
--select @TotalCliente = sum(Total) from linpedidovta where [nº factura] = @NumFactura Comentado por Carlos 05/09/02. Vale la línea de abajo
set @NumAsiento = 1
-- David Sanchez 01/06/05... todo lo referente a los inmovilizados los meto como un union
-- 700
--declare crsContabProd cursor fast_forward for select CtaVentas,CtaAbonosVta,sum(round(ImporteBruto,2)) as ImporteBruto,Delegación,[forma venta] from vstContabilizarFacturaVta where empresa = @Empresa and NumFactura = @NumFactura group by ctaventas,ctaabonosvta,delegación,[forma venta] -- 03/09/02 Quito porque me da descuadres
declare crsContabProd cursor fast_forward for select CtaVentas,CtaAbonosVta,sum(round(ImporteBruto,2)) as ImporteBruto,Delegación,[forma venta] from vstContabilizarFacturaVta where empresa = @Empresa and NumFactura = @NumFactura and ctaventas is not null group by ctaventas,ctaabonosvta,delegación,[forma venta] 
open crsContabProd
fetch next from crsContabProd into @CtaVentas,@CtaAbonosVta,@ImporteBruto,@delegación,@formaventa
while @@fetch_status = 0 begin
	if @ImporteBruto < 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaAbonosVta,1,'Abono '+@NumFactura,-@ImporteBruto,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
	if @ImporteBruto >= 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaVentas,1,'Factura '+@NumFactura,@ImporteBruto,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
	if @@error != 0 begin
		raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
		rollback
		close crsContabProd
		deallocate crsContabProd
		return(-1)
	end
	fetch next from crsContabProd into @CtaVentas,@CtaAbonosVta,@ImporteBruto,@delegación,@formaventa
end -- while @@fetch_status = 0
close crsContabProd
deallocate crsContabProd

-- David Sanchez... 13/07/06..actualizo ahora los tipos de linea 2
update LinPedidoVta set [nº factura] = @NumFactura,[fecha factura] = @fecha,estado = 4 where número = @pedido and estado = 2 and empresa=@empresa and tipolinea=2
if @@error != 0 begin
	raiserror('No se han podido actualizar las líneas de factura',11,1)

	rollback
	return(-1)
end

-- 665 Descuentos
declare @ImpDtoCliente money,@ImpDtoProducto money,@ImpDtoVta money,@ImpDtoPP money
	-- Calculo descuentos en cadena
/*
select @ImpDtoCliente = round(sum(Bruto*DescuentoCliente),2) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoProducto = round(sum(bruto*(1-(1-(descuentocliente)) * (1-(descuentoproducto)))*100/100),2) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoVta = round(sum(bruto*(1-(1-(descuentocliente)) * (1-(descuentoproducto)) * (1-(descuento))) *100/100),2) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoPP= round(sum(bruto*(1-(1-(descuentocliente)) * (1-(descuentoproducto)) * (1-(descuento)) * (1-(descuentopp))) * 100/100),2) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
*/
/*
select @ImpDtoProducto = sum(Bruto*DescuentoProducto) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura -- El de producto lo calculo para hallar el resto, pero luego lo recalculo grupo a grupo [CARLOS] 22/08/02
select @ImpDtoCliente = sum(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)))*100/100) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoVta = sum(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)) * (1-(descuento))) *100/100) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoPP= sum(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)) * (1-(descuento)) * (1-(descuentopp))) * 100/100) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
*/
-- Dejo como estaba al principio CARLOS 03/09/02
select @ImpDtoProducto = sum(round(Bruto*DescuentoProducto,2)) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura -- El de producto lo calculo para hallar el resto, pero luego lo recalculo grupo a grupo [CARLOS] 22/08/02
select @ImpDtoCliente = sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)))*100/100,2)) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoVta = sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)) * (1-(descuento))) *100/100,2)) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
select @ImpDtoPP= sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoCliente)) * (1-(descuento)) * (1-(descuentopp))) * 100/100,2)) from linpedidovta where empresa=@empresa and [nº factura] = @NumFactura
set @ImpDtoPP = @ImpDtoPP - @ImpDtoVta
--set @ImpDtoVta = @ImpDtoVta - @ImpDtoProducto
-- David Sanchez 26/03/04.. hay que restarle el de producto
-- ya que si no descudra
set @ImpDtoVta = @ImpDtoVta - @ImpDtoCliente
set @ImpDtoCliente = @ImpDtoCliente - @ImpDtoProducto
set @ImpDtoCliente=round(@ImpDtoCliente,2)
set @ImpDtoProducto = round (@ImpDtoProducto, 2)
set @impdtovta=round(@impdtovta,2)
set @impdtopp=round(@impdtopp,2)

-- Inserto líneas en PreContabildad
declare crsDtoProducto cursor fast_forward for select sum(ImpDtoProducto),CtaDescuentos from vstContabilizarFacturaVta where empresa = @Empresa and NumFactura = @NumFactura and  CtaDescuentos is not null group by CtaDescuentos
open crsDtoProducto
fetch next from crsDtoProducto into @ImpDtoProducto,@CtaDtoProductoVta
while @@fetch_status = 0 begin
	if @ImpDtoProducto < 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaDtoProductoVta,1,'Descuento Producto Abono '+@NumFactura,-@ImpDtoProducto,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
	if @ImpDtoProducto > 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaDtoProductoVta,1,'Descuento Producto Factura '+@NumFactura,@ImpDtoProducto,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)

	if @@error != 0 begin
		raiserror('No se han podido crear los asientos de descuento Producto en PreContabilidad',11,1)
		rollback
		close crsDtoProducto
		deallocate crsDtoProducto
		return(-1)
	end
	fetch next from crsDtoProducto into @ImpDtoProducto,@CtaDtoProductoVta
end
close crsDtoProducto
deallocate crsDtoProducto
if @ImpDtoCliente < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoCliente,1,'Descuento Cliente Abono '+@NumFactura,-@ImpDtoCliente,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
if @ImpDtoCliente > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoCliente,1,'Descuento Cliente Factura '+@NumFactura,@ImpDtocliente,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
if @ImpDtoVta < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDescuentoVta,1,'Descuento Venta Abono '+@NumFactura,-@ImpDtoVta,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
if @ImpDtoVta > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDescuentoVta,1,'Descuento Venta Factura '+@NumFactura,@ImpDtoVta,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
if @ImpDtoPP < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoPPVta,1,'Descuento Pronto Pago Abono '+@NumFactura,-@ImpDtoPP,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
if @ImpDtoPP > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoPPVta,1,'Descuento Pronto Pago Factura '+@NumFactura,@ImpDtoPP,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
-- 477 IVA [CARLOS] 24/06/02
declare @CtaRepercutido char(10) -- Cuenta donde se contabiliza el IVA
declare @CtaRecargoRepercutido char(10) -- Cuenta donde se contabiliza el RE (Carlos -> 31/10/02)
declare @BaseIVA money -- base imponible para ese determinado tipo de IVA
declare @PorcIVA decimal -- porcentaje de IVA para ese tipo
declare @PorcRE decimal(5,4) -- porcenatje de recargo de equivalencia para ese tipo de IVA
declare @ImporteIVA money
declare @ImporteRE money
-- David Sanchez 01/06/05 ponemos la select con un union para q me muestre las dos, y lo tratamos como si fuera una vista, porq si me saca dos cuentas de iva
declare crsIVA cursor fast_forward for select ctaRepercutido,ctaRecargoRepercutido,sum(BaseImponible) as BaseImponible ,[% IVA],[% RE] from vstContabilizarFacturaVta where empresa=@empresa and NumFactura = @NumFactura group by ctaRepercutido,[% IVA],[% RE],ctaRecargoRepercutido
open crsIVA
fetch next from crsIVA into @CtaRepercutido,@ctaRecargoRepercutido,@BaseIVA,@PorcIVA,@PorcRE
set @TotalCliente=0
while @@fetch_status=0 begin
	-- Inserto IVA
	if @CtaRepercutido is not null begin
		set @ImporteIVA = round((@BaseIVA*@PorcIVA)/100,2)
		if @BaseIVA < 0
			insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
				values (@Empresa,1,@CtaRepercutido,1,'IVA Abono '+@NumFactura,-@ImporteIVA,0,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
		if @BaseIVA >= 0
			insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
				values (@Empresa,1,@CtaRepercutido,1,'IVA Factura '+@NumFactura,0,@ImporteIVA,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
		if @@error != 0 begin
			raiserror('No se han podido crear los asientos de IVA en PreContabilidad',11,1)
			rollback
			close crsIVA
			deallocate crsIVA
			return(-1)
		end
	end else if (@PorcIVA is not null) and (@PorcIVA != 0) begin
		raiserror('Debe especificar una cuenta de IVA en los Parámetros IVA',11,1)
		rollback
		close crsIVA
		deallocate crsIVA
		return(-1)
	end
	-- Inserto Recargo Equivalencia
	if @CtaRecargoRepercutido is not null begin
		set @ImporteRE = round((@BaseIVA*@PorcRE/100),2)
		if @BaseIVA < 0
			insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
				values (@Empresa,1,@CtaRecargoRepercutido,1,'Recargo de Equivalencia Abono '+@NumFactura,-@ImporteRE,0,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
		if @BaseIVA >= 0
			insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
				values (@Empresa,1,@CtaRecargoRepercutido,1,'Recargo de Equivalencia Factura '+@NumFactura,0,@ImporteRE,@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@delegación,@formaventa, @Origen, @Usuario)
		if @@error != 0 begin
			raiserror('No se han podido crear los asientos de Recargo de Equivalencia en PreContabilidad',11,1)
			rollback
			close crsIVA
			deallocate crsIVA
			return(-1)
		end
	end else if (@PorcRE is not null) and (@PorcRE != 0) begin
		raiserror('Debe especificar una cuenta de Recargo de Equivalencia en los Parámetros IVA',11,1)
		rollback
		close crsIVA
		deallocate crsIVA
		return(-1)
	end
	if @BaseIVA is not null
		set @TotalCliente = @TotalCliente+round(@BaseIVA,2)
	if @ImporteIVA is not null
		set @TotalCliente= @TotalCliente + round(@ImporteIVA,2)
	if @ImporteRE is not null
		set @TotalCliente= @TotalCliente + round(@ImporteRE,2)
	fetch next from crsIVA into @CtaRepercutido,@CtaRecargoRepercutido,@BaseIVA,@PorcIVA,@PorcRE
end -- while 
close crsIVA
deallocate crsIVA
-- 430
declare @TotalClienteCuadre as money -- se usa sólo para cuadrar que el total del cliente no se descuadre en más de 0.02 de lo real
set @TotalClienteCuadre=0
/*
--if SYSTEM_USER = 'nuevavision\Carlos'
if SYSTEM_USER = 'sa'
	select * from vstContabilizarFacturaVta where NumFactura = @NumFactura 
*/
select @TotalClienteCuadre = sum(round(TotalAgrupado,2)) from (select sum(Total) as TotalAgrupado from vstContabilizarFacturaVta where Empresa = @Empresa and NumFactura = @NumFactura group by [% iva],[% re]) as Resultado


if abs(@TotalCliente - @TotalClienteCuadre) > 0.02 begin 
	--select * from precontabilidad where empresa='6'
	--select * from vstContabilizarFacturaVta where numfactura=@numfactura
	raiserror('Se ha producido un descuadre. Avise al Dpto. Informática',11,1)
	print 'Total cuadre: ' + STR(@TotalClienteCuadre*100)
	print 'Base Imponible: ' + str(@BaseIVA*100)
	print 'IVA: ' + str(@ImporteIVA*100)
	print 'RE: ' + str(@ImporteRE*100)
	print 'TOTAL: ' + str(@TotalCliente*100)
	rollback
	return(-3)
end


if @TotalCliente < 0 
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,delegación,formaventa,CCC,Ruta, Origen, Usuario)
		values (@Empresa,1,2,@NumCliente,@Contacto,'Abono '+@NumFactura,-@TotalCliente,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,'01/01/01',@Formapago,@vendedorReal,@DelegaciónCab,@FormaVentaCab,@CCC,@Ruta, @Origen, @Usuario)
if @TotalCliente >= 0 
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,delegación,formaventa,CCC,Ruta, Origen, Usuario)
		values (@Empresa,1,2,@NumCliente,@Contacto,'Factura '+@NumFactura,@TotalCliente,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,'01/01/01',@Formapago,@vendedorReal,@DelegaciónCab,@FormaVentaCab,@CCC,@Ruta, @Origen, @Usuario)
--set @OrigenLiq = @@identity
if @@error != 0 begin
	raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
	rollback
	return(-1)
end

-- David Sánchez... 11/07/06... Creo las lineas de tipo 2
insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen,CentroCoste,departamento, Usuario)
	select @Empresa,1,Producto,1,Texto,round(Bruto,2),@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,delegación,[Forma venta], @Origen,CentroCoste,departamento, @Usuario
	from linPedidovta 
	where número = @pedido and estado = 4 and [Nº Factura]= @NumFactura and empresa=@empresa and tipoLínea = 2  and bruto>0
if @@error != 0 begin
	raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
	rollback
	return(-1)
end
insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen,CentroCoste,departamento, Usuario)
	select @Empresa,1,Producto,1,Texto,round(-Bruto,2),@fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,delegación,[Forma venta], @Origen,CentroCoste,departamento, @Usuario 
	from linPedidovta 
	where número = @pedido and estado = 4 and [Nº Factura]= @NumFactura and empresa=@empresa and tipoLínea = 2  and bruto<0
if @@error != 0 begin
	raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
	rollback
	return(-1)
end




-- David Sanchez Lopez... 18/09/03---
-- tendremos que ver si el campo señal es true ya que si lo fuera
-- no tendremos que ajustar
-- para ello me declaro la variable señal como un bit 
-- Crear Cartera
declare @Señal bit,@NumPlazos tinyint,@DíasEntrePlazos tinyint,@MesesEntrePlazos tinyint,@TotalClienteRestante money,@ImportePlazo money,@i tinyint,@FechaEfecto datetime,@FechaDevuelta as datetime
select @Señal=señal,@NumPlazos = [nº plazos],@DíasEntrePlazos = DíasEntrePlazos,@MesesEntrePlazos=MesesEntrePlazos from PlazosPago where número = @PlazosPago and empresa=@empresa
set @TotalClienteRestante = @TotalCliente
set @ImportePlazo = round(@TotalClienteRestante / @NumPlazos,2)
set @i = 1
--if @PrimerVto>=@Fecha -- Comprobación: Carlos 30/09/02
	set @FechaEfecto = @PrimerVto -- Comento las otra lineas porque lo cambio al crear ala cab factura --> Carlos 02/10/02
--else
--	set @FechaEfecto = @Fecha
-- Hago el contraasiento a la 430
if @TotalCliente < 0 
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,delegación,formaventa,Ruta, Origen, Usuario)
		values (@Empresa,0,2,@NumCliente,@Contacto,'Paso a cartera abono '+@NumFactura,-@TotalCliente,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,'01/01/01',@Formapago,@vendedorReal,@DelegaciónCab,@FormaVentaCab,@Ruta, @Origen, @Usuario)

if @TotalCliente > 0 
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,delegación,formaventa,ruta, Origen, Usuario)
		values (@Empresa,0,2,@NumCliente,@Contacto,'Paso a cartera factura '+@NumFactura,@TotalCliente,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,'01/01/01',@Formapago,@vendedorReal,@DelegaciónCab,@FormaVentaCab,@Ruta, @Origen, @Usuario)
if @@error != 0 begin
	raiserror('No se ha podido crear el contraasiento de cartera',11,1)
	rollback
	return(-1)
end
set @FechaDevuelta = @FechaEfecto
-- David Sanchez 14/04/03.. Le meto la ruta del contacto de cobro en vez de la del pedido
declare @RutaCobro as char(3)
set @RutaCobro=(select ruta from clientes where empresa=@empresa and [nº cliente]=@NumCliente and contacto=@ContactoCobro)
declare @CrearEfectosAuto as bit = 1
if exists (select * from EfectosPedidoVenta where Empresa = @Empresa and Pedido = @Pedido) begin
	if (select SUM(Importe) from EfectosPedidoVenta where Empresa = @Empresa and Pedido = @Pedido) = @TotalCliente begin
		select @NumPlazos = COUNT(*) from EfectosPedidoVenta where Empresa = @Empresa and Pedido = @Pedido
		insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,efecto,delegación,formaventa,CCC,Ruta, Origen, Usuario)
			select @Empresa,2,2,@NumCliente,@ContactoCobro,'Efecto '+ltrim(str(ROW_NUMBER() over (order by FechaVencimiento, Id)))+'/'+ltrim(str(@NumPlazos))+
				case when @TotalCliente >= 0 then ' factura ' else ' abono ' end
				+@NumFactura,Importe,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,FechaVencimiento,FormaPago,@vendedorReal,ltrim(str(ROW_NUMBER() over (order by FechaVencimiento, Id))),@DelegaciónCab,@FormaVentaCab,CCC,@RutaCobro, @Origen, @Usuario
			from EfectosPedidoVenta where Empresa = @Empresa and Pedido = @Pedido
		if @@error != 0 begin
			raiserror('No se ha podido crear la cartera manual',11,1)
			rollback
			return(-1)
		end
		set @CrearEfectosAuto = 0
	end
end
while (@CrearEfectosAuto = 1 and @i <= @NumPlazos) begin
	-- David Sanchez 14/04/03.. Le meto el contacto cobro
	if @i = @NumPlazos
		set @ImportePlazo = @TotalClienteRestante
	-- Carlos 17/11/21: no permitimos cobros en efectivo de más de 1000 €
	if abs(@ImportePlazo) >= 1000 and @Empresa != '3' and @FormaPago = 'EFC' begin
		raiserror('Los cobros en efectivo de ese importe no están permitidos. Negocie otra forma de pago con el cliente.',11,1)
		rollback
		return(-1)
	end
	if @ImportePlazo < 0 
		insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,efecto,delegación,formaventa,CCC,Ruta, Origen, Usuario)
			values (@Empresa,2,2,@NumCliente,@ContactoCobro,'Efecto '+ltrim(str(@i))+'/'+ltrim(str(@NumPlazos))+' abono '+@NumFactura,-@ImportePlazo,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@FechaDevuelta,@Formapago,@vendedorReal,ltrim(str(@i)),@DelegaciónCab,@FormaVentaCab,@CCC,@RutaCobro, @Origen, @Usuario)
	if @ImportePlazo > 0 
		insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,efecto,delegación,formaventa,CCC,Ruta, Origen, Usuario)
			values (@Empresa,2,2,@NumCliente,@ContactoCobro,'Efecto '+ltrim(str(@i))+'/'+ltrim(str(@NumPlazos))+' factura '+@NumFactura,@ImportePlazo,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@FechaDevuelta,@Formapago,@vendedorReal,ltrim(str(@i)),@DelegaciónCab,@FormaVentaCab,@CCC,@RutaCobro, @Origen, @Usuario)
	if @@error != 0 begin
		raiserror('No se ha podido crear la cartera',11,1)
		rollback
		return(-1)
	end
	set @TotalClienteRestante = @TotalClienteRestante - @ImportePlazo
	if not(@i=1 and @señal=1) begin -- David Sanchez si es i=1 y ademas es señal no se suma.
		-- David Sanchez 30/09/03.. Primero sumamos los meses y despues los dias
		set @FechaEfecto=dateadd(m,@MesesEntrePlazos,@FechaEfecto)
		set @FechaEfecto = @FechaEfecto + @DíasEntrePlazos
	end
	-- David 14/04/03.. Le paso el contacto cobro
	exec prdAjustarDíasPagoCliente @Empresa, @NumCliente,@ContactoCobro,@FechaEfecto,@FechaDevuelta output --[CARLOS] 30/07/02
	if @@error != 0 begin
		raiserror('Error al ajustar vencimiento',11,1)
		rollback
		return(-1)	
	end
--	set @FechaEfecto = @FechaDevuelta
	set @i = @i + 1
end

-- Carlos 26/08/20: Prepagos
declare @idPrepago as int
declare @importePrepago as money
declare @cuentaPrepago as char(10)
declare @conceptoPrepago as nvarchar(50)

select top 1 @idPrepago = Id, @importePrepago = Importe, @cuentaPrepago = CuentaContable, @conceptoPrepago = ConceptoAdicional 
from Prepagos 
where Empresa = @Empresa and Pedido = @Pedido and Factura is null and Importe != 0

set @conceptoPrepago = left('Prepago '+ @conceptoPrepago, 50)

if @idPrepago is not null begin
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],contacto,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],fechavto,formapago,vendedor,efecto,delegación,formaventa,Ruta, Origen, Contrapartida, Usuario)
		--values (@Empresa,3,2,@NumCliente,@ContactoCobro,@conceptoPrepago,@importePrepago,@Fecha,@NumFactura,@NumAsiento,'_FRAVTA',1,@Fecha,@Formapago,@vendedor,1,@DelegaciónCab,@FormaVentaCab,@RutaCobro, @Origen, @cuentaPrepago)
		select Empresa, 3, 2, @NumCliente, @ContactoCobro, left('Prepago '+ ConceptoAdicional, 50), Importe, @Fecha, @NumFactura, @NumAsiento,'_FRAVTA',1,@Fecha,@Formapago,@vendedorReal,1,@DelegaciónCab,@FormaVentaCab,@RutaCobro, @Origen, CuentaContable, @Usuario
			from Prepagos 
			where Empresa = @Empresa and Pedido = @Pedido and Factura is null and Importe != 0
	update Prepagos set Factura = @NumFactura where Empresa = @Empresa and Pedido = @Pedido and Factura is null and Importe != 0
end

-- David Sanchez 12/08/4
-- llamamos a comprobar los retendios
-- este ya modifica en el extracto
declare @DevueltoRetenidos as int
exec @DevueltoRetenidos=prdComprobarRetenidosFacturaVta @empresa,@NumFactura
if @DevueltoRetenidos<0 begin
	rollback
	set @NumFactura=''
end 

/*
commit
return 2
*/

-- David Sanchez.. .03-06-05.. llamamos  a que nos contabilice el inmovilizado
-- Lo tenemos que hacer solo si hay inmovilizado
if (select count(*) from linpedidovta where numero=@pedido and [nº factura]=@numfactura and estado=4 and tipolinea=3)>0 begin
	declare @DevueltoInmo as int
	exec @DevueltoInmo=prdContabilizarVentaInmovilizado @empresa,@NumFactura, '_FRAVTA',@fecha,@NumAsiento,@DelegacionCab,@FormaVentaCab,@origen
	if @DevueltoInmo<0 begin
		rollback
		set @NumFactura=''
		raiserror('Error al contabilizar el inmovilizado',11,1)
		return(-1)
	end
end
-- Contabilizar
declare @UltAsiento int -- para guardar el asiento del apunte 17/06/02


if system_user='NUEVAVISION\Carlos' begin
--if system_user='sa' begin
	--update precontabilidad set debe = debe+.01 where diario = '_fravta' and [nº cuenta] = '70000199'
--if @empresa = '4' 
--  update precontabilidad set debe = debe-.03 where diario = '_fravta' and [nº cuenta] = '70000108'-- and concepto like 'Descuento Venta Factura%'
  select * from precontabilidad where diario='_FRAVTA'
  select sum(debe-haber) from precontabilidad where diario='_FRAVTA'
end



exec @UltAsiento = prdContabilizar @Empresa, '_FRAVTA', @Usuario
if @@error < 0 begin
	rollback
	set @NumFactura=''
	raiserror('Error al contabilizar',11,1)
	return(-1)
END ELSE IF @UltAsiento<=0 begin
	rollback
	set @NumFactura=''
	raiserror('Error al contabilizar',16,1)
	return(-1)
end

-- Una vez contabilizado, tendre que buscar las lineas de extracto inmovilizado, que tengan este numero de factura en el numero de documento, y que sean de tipo de apunte 3
-- y hacer un update
update extractoinmovilizado set asiento=@UltAsiento where empresa=@empresa and tipoApunte=3 and [NºDocumento]=@NumFactura
if @@Error!=0 begin
	rollback
	set @NumFactura=''
	raiserror('Error al contabilizar el inmovilizado',11,1)
	return -1
end

-- David Sanchez 14-2-7 ... insertamos las lineas en linRenting en el caso de que lo sea
if (select count(*) from cabrenting where numero=@pedido and estado=2)>0 begin
	insert into linRenting (empresa,numero,texto,fecha,baseimponible,total, Usuario)
		select @Empresa,@Pedido,'Factura ' + ltrim(rtrim(@NumFactura)),getdate(),sum([base imponible]),sum(Total), @Usuario 
		from linpedidovta as l inner join cabrenting as c
			on l.empresa=c.empresa and l.numero=c.numero
				where c.numero=@Pedido
		if @@Error!=0 begin
			rollback
			set @NumFactura=''
			raiserror('Error al insertar el renting',11,1)
			return -1
		end

	insert into linRenting (empresa,numero,texto,fecha,baseimponible, Usuario)
		select @Empresa,@Pedido,'Factura '+ ltrim(rtrim(@NumFactura)),getdate(),-sum(coste*cantidad), @Usuario 
		from linpedidovta as l inner join cabrenting as c
			on l.empresa=c.empresa and l.numero=c.numero
				where c.numero=@Pedido

		if @@Error!=0 begin
			rollback
			set @NumFactura=''
			raiserror('Error al Insertar el renting',11,1)
			return -1
		end

	-- Actualizamos cabrentin y le ponemos el estado=3
	update cabrenting set estado=3 where numero=@pedido and estado=2

		if @@Error!=0 begin
			rollback
			set @NumFactura=''
			raiserror('Error al Insertar el renting',11,1)
			return -1
		end

end

-- Liquidar (17/06/02)
if @TotalCliente <>0 begin
	declare @OrigenLiq int -- nº orden de la factura y el apunte que la anula para liquidarlos
	declare @DestinoLiq int -- nº orden de la factura y el apunte que la anula para liquidarlos
	select @OrigenLiq = [nº orden] from extractocliente where empresa = @empresa and asiento=@UltAsiento and tipoapunte=1 -- factura
	select @DestinoLiq = [nº orden] from extractocliente where empresa = @empresa and asiento=@UltAsiento and tipoapunte=0 -- blanco
	/*
	print 'Prueba fact'
	print 'Asiento: ' + str(@UltAsiento)
	print 'Origen: ' + str(@origenliq)
	print 'Destino ' + str(@destinoliq)
	*/
	exec prdLiquidar @Empresa,@OrigenLiq,@DestinoLiq

	-- Carlos 26/08/20: liquidamos el prepago
	if @idPrepago is not null begin
		declare @OrigenLiqPrepago int -- nº orden del efecto y el apunte que la anula para liquidarlos
		declare @DestinoLiqPrepago int -- nº orden del pago y el apunte que la anula para liquidarlos
		select top 1 @OrigenLiqPrepago = [nº orden] from extractocliente where empresa = @empresa and asiento=@UltAsiento and tipoapunte=2 -- efecto
		select @DestinoLiqPrepago = [nº orden] from extractocliente where empresa = @empresa and asiento=@UltAsiento and tipoapunte=3 -- pago
		
		exec prdLiquidar @Empresa,@OrigenLiqPrepago,@DestinoLiqPrepago
	end
end 


-- Enviar aviso abono: Carlos (04/04/12)
declare @Destinatarios as char(162)
declare @CuerpoCorreo as char(1000)
declare @ComentariosPedido as char(1000)
declare @NombreCliente as char(50)
declare @DirecciónCliente as CHAR(50)
DECLARE @tableHTML  NVARCHAR(MAX) ;
/*
if @TotalCliente < 0 begin
	set @Destinatarios = dbo.correovendedor(@Vendedor)
	select @CuerpoCorreo = 'Se ha realizado un abono de ' +rtrim(cast(@TotalCliente as char))+' € (IVA incluido) al cliente '+@NumCliente
	select @ComentariosPedido = (
		select top 1 CAST(comentarios AS CHAR(1000)) from cabpedidovta AS c inner join linpedidovta AS l
		on c.empresa=l.empresa and c.número = l.número 
		where l.empresa = @empresa and l.[Nº Factura] = @NumFactura
		)
	if @ComentariosPedido is null or ltrim(rtrim(@ComentariosPedido))='' begin
			rollback
			set @NumFactura=''
			raiserror('En las facturas rectificativas es obligatorio poner un comentario con el motivo de la devolución',11,1)
			return -1
	end
	select @nombrecliente = nombre, @direccióncliente = dirección from clientes where empresa = @Empresa and [nº cliente] = @NumCliente and contacto = @Contacto
	
	
	SET @tableHTML =
    N'<H1>Informe de Factura Rectificativa</H1>' +
    N'<H2>'+@CuerpoCorreo+'</H2>' +
    N'<p>'+@ComentariosPedido+'</p>' +
    N'<p></p>' +
    N'<p><b>'+@NombreCliente+'</b></p>' +
    N'<p>'+@DirecciónCliente+'</p>' +   
    N'<table border="1">' +
    N'<tr><th>Producto</th><th>Texto</th>' +
    N'<th>Base Imponible</th><th>Forma de Venta</th></tr>' +
    CAST ( ( SELECT td = producto,       '',
					td = texto, '',
					td = [base imponible], '',
					td = [forma venta], ''
 					from linpedidovta where empresa = @empresa and [Nº Factura] = @NumFactura
				
              
			FOR XML PATH('tr'), TYPE 
	) AS NVARCHAR(MAX) ) +
	N'</table>' ;
	

	
	
	EXEC msdb.dbo.sp_send_dbmail
    @profile_name = 'Nesto',
    @recipients = @Destinatarios,
    @copy_recipients = 'carlosadrian@nuevavision.es; manuelrodriguez@nuevavision.es; eloisa@nuevavision.es; enriqueadrian@nuevavision.es',
	@body = @tableHTML,
	@body_format = 'HTML',
    @subject = 'Aviso de Factura Rectificativa' ;
end
*/
/*
-- Enviar aviso factura por correo electrónico (cargo 22). Carlos 10/05/12
declare @CorreoCargo as char(162)
select @CorreoCargo = (select top 1 CorreoElectrónico from personascontactocliente where empresa = @empresa and [nºcliente] = @numcliente and contacto = @Contacto and cargo = '22')

if @CorreoCargo is not null begin
	declare @enlace as nvarchar(255)
	if @TotalCliente > 0 begin
		set @enlace = 'https://www.paypal.com/cgi-bin/webscr?cmd=_xclick'
		set @enlace = @enlace + '&business=nuevavision@nuevavision.es'
		set @enlace = @enlace + '&currency_code=EUR'
		set @enlace = @enlace + '&amount='+ltrim(cast(@TotalCliente as char))
		set @enlace = @enlace + '&item_name='+@NumFactura
	end else begin
		set @enlace = 'Importe negativo, no hay enlace'
	end

	SET @tableHTML =
    N'<H1>Factura Electrónica</H1>' +
    N'<H2>No imprimir la factura en papel</H2>' +
    N'<p>Hay que enviar la factura '+rtrim(@NumFactura)+' del cliente '+ rtrim(@NumCliente) +' a '+ rtrim(@CorreoCargo) +'.</p>' +
	N'<p>Enlace de pago: '+ @enlace +'.</p>' +
    N'<p></p>';

	
	
	EXEC msdb.dbo.sp_send_dbmail
    @profile_name = 'Nesto',
    @recipients = 'administracion@nuevavision.es',
    --@copy_recipients = 'carlosadrian@nuevavision.es',
	@body = @tableHTML,
	@body_format = 'HTML',
    @subject = 'Enviar Factura por Correo Electrónico' ;
end
*/

-- Carlos 30/11/2018. Si es un renting, lo metemos en la tabla de rentings
if @NumCliente in ('30722', '40661') begin
	insert into RentingFacturas select @NumFactura
end


commit transaction
-- FIN TRANSACCIÓN

-- Carlos 16/01/25: comprobamos si el cliente está en un estado que deba pasar a cero
-- Verificar si el estado requiere envío de correo
DECLARE @EmailSubject NVARCHAR(100) = 'Notificación de Recuperación de Cliente a estado 0'
DECLARE @EmailBody NVARCHAR(MAX)
    
-- Carlos 08/10/26: Estado 95 (una sola compra, por Amazon) solo se recupera si el pedido trae algo que no sea de Amazon (Forma Venta <> STK)
IF (@EstadoCliente IN (11, 47, 53, 58, 62, 68, 71, 42) OR (@EstadoCliente = 95 AND EXISTS (select * from LinPedidoVta l where l.Empresa = @Empresa and l.Número = @Pedido and l.[Forma Venta] <> 'STK' and l.[base imponible] > 0))) and @TotalCliente > 0 and exists (select * from LinPedidoVta l where l.Empresa = @Empresa and l.Número = @Pedido and l.Grupo != 'PEL' and [base imponible] > 0)
BEGIN
    -- Configuración del correo
    set @Destinatarios = dbo.correovendedor(@Vendedor)

    -- Construir el cuerpo del correo en HTML
    SET @EmailBody = N'
    <html>
    <body style="font-family: Arial, sans-serif;">
        <h2 style="color: #2c3e50;">Notificación de Compra</h2>
        <p>Se ha registrado una compra de un cliente con estado: <strong>' + 
        CAST(@EstadoCliente AS NVARCHAR(10)) + '</strong></p>
        <hr>
        <p style="color: #7f8c8d; font-size: 12px;">El cliente '+rtrim(@NumCliente)+'/'+rtrim(@Contacto)+' se ha recuperado a estado 0.</p>
    </body>
    </html>'
    
    -- Enviar el correo usando sp_send_dbmail
    EXEC msdb.dbo.sp_send_dbmail
        @profile_name = 'Nesto', -- Ajusta esto a tu perfil de correo configurado
        @recipients = @Destinatarios,
		@copy_recipients = 'direccion@nuevavision.es; albertosancho@nuevavision.es; tiendaonline@nuevavision.es',
        @subject = @EmailSubject,
        @body = @EmailBody,
        @body_format = 'HTML' -- Especificamos que el formato es HTML

	update Clientes set Estado = 0 where Empresa = '1' and [Nº Cliente] = @NumCliente and Contacto = @Contacto
END

-- Carlos 09/10/26: cliente nuevo cuya única compra es esta, toda de Amazon (Forma Venta STK) → estado 95 «Una sola compra, por Amazon», sin correo
IF @Empresa = 1 AND @EstadoCliente IN (0, 9)
	AND EXISTS (select * from LinPedidoVta l where l.Empresa = @Empresa and l.[Nº Factura] = @NumFactura and l.[base imponible] > 0)
	AND NOT EXISTS (select * from LinPedidoVta l where l.Empresa = @Empresa and l.[Nº Factura] = @NumFactura and l.[Forma Venta] <> 'STK')
	AND NOT EXISTS (select * from LinPedidoVta l where l.Empresa = @Empresa and l.[Nº Cliente] = @NumCliente and l.Contacto = @Contacto and l.Estado = 4 and l.Número <> @Pedido)
BEGIN
	update Clientes set Estado = 95 where Empresa = '1' and [Nº Cliente] = @NumCliente and Contacto = @Contacto and Estado IN (0, 9)
END

declare @EstadoPeluqueria smallint
declare @VendedorPeluqueria char(3)
select @EstadoPeluqueria = Estado, @VendedorPeluqueria = Vendedor from VendedoresClienteGrupoProducto where Empresa = @Empresa and Cliente = @NumCliente and Contacto = @Contacto and GrupoProducto = 'PEL' 

IF @EstadoPeluqueria is not null and @EstadoPeluqueria = 11 and exists (select * from LinPedidoVta l where l.Empresa = @Empresa and l.Número = @Pedido and l.Grupo = 'PEL' and [base imponible] > 0) and @TotalCliente > 0
BEGIN
	set @Destinatarios = dbo.correovendedor(@VendedorPeluqueria)
		
	-- Construir el cuerpo del correo en HTML
	SET @EmailBody = N'
	<html>
	<body style="font-family: Arial, sans-serif;">
		<h2 style="color: #2c3e50;">Notificación de Compra</h2>
		<p>Se ha registrado una compra de un cliente con estado de peluquería: <strong>' + 
		CAST(@EstadoPeluqueria AS NVARCHAR(10)) + '</strong></p>
		<hr>
		<p style="color: #7f8c8d; font-size: 12px;">El cliente '+rtrim(@NumCliente)+'/'+rtrim(@Contacto)+' se ha recuperado a estado 0.</p>
	</body>
	</html>'
    
	-- Enviar el correo usando sp_send_dbmail
	EXEC msdb.dbo.sp_send_dbmail
		@profile_name = 'Nesto', -- Ajusta esto a tu perfil de correo configurado
		@recipients = @Destinatarios,
		@copy_recipients = 'direccion@nuevavision.es; tiendaonline@nuevavision.es',
		@subject = @EmailSubject,
		@body = @EmailBody,
		@body_format = 'HTML' -- Especificamos que el formato es HTML

	update VendedoresClienteGrupoProducto set Estado = 0 where Empresa = '1' and Cliente = @NumCliente and Contacto = @Contacto and GrupoProducto = 'PEL'
END

-- Carlos 10/07/19: si es de cursos, mandamos correo al vendedor
if (@Serie = 'CV' and exists (
		select * from linpedidovta where empresa = @empresa and [Nº Factura] = @NumFactura and (Producto <> '70500001' or Delegación<>'ALG')
	)) begin
	set @Destinatarios = dbo.correovendedor(@Vendedor)
	select @CuerpoCorreo = 'Al cliente '+rtrim(@NumCliente)+'/'+rtrim(@Contacto)+' se le ha facturado un curso.'
	
	select @nombrecliente = nombre, @direccióncliente = dirección from clientes where empresa = @Empresa and [nº cliente] = @NumCliente and contacto = @Contacto
	
	
	SET @tableHTML =
    N'<H1>Curso Facturado</H1>' +
    N'<H2>'+@CuerpoCorreo+'</H2>' +
    N'<p></p>' +
    N'<p><b>'+@NombreCliente+'</b></p>' +
    N'<p>'+@DirecciónCliente+'</p>' +   
    N'<table border="1">' +
    N'<tr><th>Pedido</th><th>Curso</th><th>Texto</th>' +
    N'<th>Base Imponible</th><th>Almacén</th><th>Forma de Venta</th></tr>' +
    CAST ( ( SELECT td = Número,       '',
					td = producto,       '',
					td = texto, '',
					td = FORMAT([base imponible], N'C', N'es-ES'), '',
					td = Almacén, '',
					td = [forma venta], ''
 					from linpedidovta where empresa = @empresa and [Nº Factura] = @NumFactura
				
              
			FOR XML PATH('tr'), TYPE 
	) AS NVARCHAR(MAX) ) +
	N'</table>' ;
	

	declare @asunto nvarchar(100) = 'Curso facturado al c/ ' + rtrim(@NumCliente)
	
	EXEC msdb.dbo.sp_send_dbmail
    @profile_name = 'Nesto',
    @recipients = @Destinatarios,
    @copy_recipients = 'cursos@nuevavision.es',
	@body = @tableHTML,
	@body_format = 'HTML',
    @subject = @asunto;

end

GO

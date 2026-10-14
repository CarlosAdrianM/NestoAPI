-- 14/10/26: las devoluciones a proveedor de productos de grupos sin ProductoBonificacion (MTP, CUR...)
-- bajaban el stock (ExtractoProducto) pero NO el hueco (Ubicaciones estado 0).
-- Causa: «l.producto<>g.productobonificacion» con productobonificacion NULL no es verdadero, así que la
-- línea no entraba en #ubi. Se cambia por isnull(g.productobonificacion,'') en los dos insert a #ubi.
-- Caso real: albaranes 125581/125582 del 13/10/26 (productos 23101, 37182, 38282).
--
-- El SP está creado con ANSI_NULLS OFF y QUOTED_IDENTIFIER OFF (usa "_ALBCOMP" entre comillas dobles):
-- hay que mantenerlos o el exec a prdExtrProducto cambia de significado.
SET ANSI_NULLS OFF
GO
SET QUOTED_IDENTIFIER OFF
GO
ALTER PROCEDURE [dbo].[prdCrearAlbaránCmp] @Empresa char(3), @Pedido int, @FechaRecepción datetime, @ImporteMínimo as money, @Contabiliza  bit = 0, @Usuario varchar(30) = NULL  AS
-- David Sanchez....16/05/03
-- El parametro contabiliza lo uso para ver si se tiene que contabilizar le albaran o no.
-- Igualandolo a cero actua como un opcional
-- Cuando llame a este procedimiento desde le boton de crear albaran, le pasare un uno a este parametro para que me lo contabilice.
-- Cuando creo la factura y el albaran a la vez no lo pasare, ya que no valdria para nada.
/*
Códigos de Return:
-3: Importe del albarán menor a importe mínimo (09/07/02)
*/
/*
if system_user!='DOMINIO\David' begin
	raiserror('No disponible. Intenta más tarde',11,1)
	return -2 -- sale del procedimiento con error
end
*/

-- Carlos 11/04/24: si no pasan el parámetro usuario ponemos el del sistema
if @Usuario is null OR CHARINDEX('$', SUSER_NAME()) = 0 begin
	set @Usuario = SYSTEM_USER
end




declare @Espejo as char(3)
set @espejo=(select [iva por defecto] from empresas where número=@empresa)
declare @Fecha datetime
set @Fecha = getdate()
declare @FechaSinHora as datetime
set  @FechaSinHora =str(day(@Fecha))+'/'+str(month(@Fecha))+'/'+str(year(@Fecha))+' 00:00:00'
declare @Origen as char(3)
declare @ProductoUbicacion as char(10)
--set @ProductoUbicacion ='12375'
-- Comprobar si hay líneas para albaranear
if (select count(*) from linpedidoCmp where empresa=@empresa and número = @Pedido and estado = 1 and fecharecepción<=@fechaRecepción and VistoBueno=1) < 1 begin
	raiserror('No hay líneas para albaranear',11,1)
	return -2 -- sale del procedimiento con error
end

-- David Sánchez... 8-05-08... miramos si hay productos que necesiten el numero de serie para poder albaranearlos
if (select count(*) from linpedidoCmp as l inner join productos as p
	on l.empresa=p.empresa and l.producto=p.numero
	where 
	l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.fecharecepción<=@fechaRecepción and l.VistoBueno=1
	and p.necesitanumserie=1 and (l.numserie is null or ltrim(rtrim(l.numserie))=''))>0 begin

	raiserror('Hay productos que necesitan el número de serie ',11,1)
	return -2 -- sale del procedimiento con error
end


-- David Sanchez 01/02/05
-- Esto lo quitamos ya que hemos bloqueado para que no se puede cambiar
-- un kit siempre y cuando haya stock, por lo que ya no se nos fastidian los precios medios
/*
declare @Kit as integer
-- hay q comprobar que ningun producto sea un kit
-- David Sanchez.. 20/10/04... ponemos el numero de kit
if (select count(número) from kits where número in (select producto from linpedidocmp  where empresa=@empresa and número = @Pedido and estado = 1 )and empresa=@empresa)>0 begin -- si es un kit
	set @kit=(select top 1 número from kits where número in (select producto from linpedidocmp  where empresa=@empresa and número = @Pedido and estado = 1 )and empresa=@empresa)
	raiserror('No se puede albaranear un kit %d',11,1,@Kit)
	return -2 -- sale del procedimiento con erro
end 
*/
-- Comprobar Importe Mínimo
-- [CARLOS] 09/07/02
if @ImporteMínimo <> 0 begin -- el importe mínimo 0 se considera como sin importe mínimo
	declare @ImporteAlbarán as money
	select @ImporteAlbarán = sum(BaseImponible) from linpedidoCmp where empresa=@empresa and número = @Pedido and (estado >= 1 and estado <=2) and fecharecepción<=@fecharecepción and VistoBueno=1
		-- pongo el estado 2 también en la select porque también se va a facturar y al fin y al cabo es el importe de la factura lo que cuenta
	if @ImporteAlbarán < @ImporteMínimo begin
		-- pongo el pedido como pendiente cuando no llega al importe mínimo ([CARLOS] 03/09/02)
		update linpedidoCmp set estado=-1 where empresa=@empresa and número = @Pedido and estado = 1 and fechaRecepción<=@fechaRecepción and VistoBueno=1
		-- salgo del procedimiento sin crear el albarán
		raiserror('Importe del albarán es menor al importe mínimo',1,1)
		return -3 -- sale del procedimiento con error -3 específico para designar que el importe es insuficiente
	end
end
-- Crear cabecera de albarán y actualizar líneas pedido
declare @UltNumAlbarán int,@NumProveedor char(10),@Contacto char(3),@PeriodoFacturación as char(3),@FormaPago as char(3),@NumDocProv as char(50)
select @UltNumAlbarán = AlbaranesCmp from ContadoresGlobales
select @NumProveedor = nºproveedor,@contacto = Contacto,@PeriodoFacturación = PeriodoFacturación,@origen=empresa,@FormaPago=FormaPago,@NumDocProv=[NºDocumentoProv]  from CabPedidoCmp where empresa=@empresa and número = @pedido
-- Calcular Cuentas de Descuentos
declare @CtaDtoProveedor char(10),@CtaDtoProductoCmp char(10),@CtaDescuentoCmp char(10),@CtaDtoPPCmp char(10)
	--Comentada por Carlos 22/08/02 porque ahora los descuentos de producto van en el GruposProducto @CtaDtoProductoVta. Linea original:
	--select @CtaDtoCliente = CtaDtoCliente,@CtaDtoProductoVta = CtaDtoProductoVta,@CtaDescuentoVta = CtaDescuentoVta,@CtaDtoPPVta = CtaDtoPPVta from empresas where número = @Empresa
select @CtaDtoProveedor = CtaDtoProveedor,@CtaDescuentoCmp = CtaDescuentoCmp,@CtaDtoPPCmp = CtaDtoPPCmp from empresas where número = @Empresa
--print 'Prov: ' + @NumProveedor
--print 'Cont: ' + @Contacto
-- COMIENZA TRANSACCIÓN -------------------------------------------------------------------------------------------------------------------
begin transaction
update ContadoresGlobales set AlbaranesCmp = @UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido actualizar el contador de albaranes',11,1)
	rollback
	return -1
end

-- David Sanchez 8/05/08
-- Insertamos en la tabla numero de serie
insert into numerosserie (producto,numserie)
select producto,numserie from linpedidoCmp as l inner join productos as p
on l.empresa=p.empresa and l.producto=p.numero
where 
l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.fecharecepción<=@fecharecepción and l.VistoBueno=1
and p.necesitanumserie=1 and (l.numserie is not null and ltrim(rtrim(l.numserie))<>'')

if @@error != 0 begin
	raiserror('No se ha podido insertar los numeros de serie',11,1)
	rollback
	return -1
end



-- David Sanchez Lopez... 18/05/04
-- Si el almacen en el que estamos tiene control ubicaciones
-- lo hacemos
declare @Ubicacion as int
if (select top 1 a.controlubicaciones from linpedidocmp as l inner join almacenes as a on l.empresa=a.empresa and l.almacen=a.número
	where  l.empresa=@empresa and l.número = @Pedido and l.estado = 1 and l.[fecharecepcion]<=@fecharecepción and l.VistoBueno=1 )!=0 begin
if @@error != 0 begin
	raiserror('No se ha podido determinar la ubicación',11,1)
	rollback
	return -1
end
	-- tendremos que buscar si hay algun producto que tenga mas de una ubicacion, y si fuera asi no le dejaremos 
	-- filtramos por que el pedido sea nulo
	if @espejo is  null begin 
		if (select count(*) from (select numero,almacen,count(*) as contador from (
			select u.número,u.almacen,u.pasillo,u.fila,u.columna  from ubicaciones as u inner join linpedidocmp as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur
			on l.empresa=ur.empresa and l.numero=ur.numerocmp and l.[nºorden]=ur.[nºordencmp]
			where u.estado=0  and  l.número=@pedido and l.estado=1 and l.VistoBueno=1 and l.[fecharecepcion]<=@fecharecepción  and  l.tipolinea=1 and l.cantidad<0 and ur.[nºordencmp] is null
			group by u.número,u.almacen,u.pasillo,u.fila,u.columna) as a group by numero,almacen having count(*)>1) as b)=0  begin
		
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
			set @ubicacion=0
		end else begin
			set @Ubicacion=1
		end
	
	end else begin
		
	if (select count(*) from (select numero,almacen,count(*) as contador from (
			select u.número,u.almacen,u.pasillo,u.fila,u.columna  from ubicaciones as u inner join linpedidocmp as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur
			on l.empresa=ur.empresa and l.numero=ur.numerocmp and l.[nºorden]=ur.[nºordencmp]
			where u.estado=0  and (u.empresa=@empresa or u.empresa=@espejo) and l.número=@pedido and l.VistoBueno=1 and  l.estado=1 and l.[fecharecepcion]<=@fecharecepción  and  l.tipolinea=1 and l.cantidad<0 and ur.[nºordencmp] is   null
			group by u.número,u.almacen,u.pasillo,u.fila,u.columna) as a group by numero,almacen having count(*)>1) as b)=0  begin
		
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end		
			set @ubicacion=0
		end else begin
			set @Ubicacion=1
		end
	end
	-- si tiene mas de una ubicacion le mostramos un error 
	if @ubicacion=1 begin
		rollback
		raiserror('50001 Hay productos con varias ubicaciones',11,1)
		return -1
	end else begin
		-- David Sanchez 19/05/04... Lo tenemos que hacer con una tabla temporal, ya que lo que hacemos es un update
		if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Ubi') ) drop table [dbo].[#Ubi]
		CREATE TABLE [#Ubi] 
			([empresa] [char] (3) NOT NULL ,
			[Almacen] [char] (3) NOT NULL ,
			[Producto] [char] (10) NOT NULL ,
			[Cantidad]  [integer],
			[Pasillo] [char] (3),
			[Fila] [char] (3),
			[Columna] [char] (3)
		)	
	      	ON [PRIMARY] 
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
		
		-- insertamos primero las que no estan reservadas
		/* -- Carlos 08/01/26: corregimos bug en el where -> having
		insert into #ubi (empresa,almacen,producto,cantidad) 
			select l.empresa,l.almacen,l.producto,sum(l.cantidad) from linpedidocmp as l inner join gruposproducto as g
				on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
				left join ubicacionesreservadas as u
				on l.empresa=u.empresa and l.numero=u.numerocmp and l.[nºorden]=u.nºordencmp
				where l.empresa=@empresa and l.número=@pedido and  l.estado=1 and l.VistoBueno=1 and l.[fecharecepcion]<=@fecharecepción and l.cantidad<0 and u.nºordencmp is null
					 group by l.empresa,l.almacen,l.producto 
		*/
		insert into #ubi (empresa,almacen,producto,cantidad) 
		select l.empresa,l.almacen,l.producto,sum(l.cantidad) from linpedidocmp as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>isnull(g.productobonificacion,'') -- Carlos 14/10/26: con NULL (MTP) no entraba y no se restaba del hueco
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numerocmp and l.[nºorden]=u.nºordencmp
			where l.empresa=@empresa and l.número=@pedido and l.estado=1 and l.VistoBueno=1 
				and l.[fecharecepcion]<=@fecharecepción and u.nºordencmp is null
				 group by l.empresa,l.almacen,l.producto 
				 having sum(l.cantidad) < 0  -- Solo si el neto es negativo

		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
		
		-- Ponemos el pasillo, la fila y la columna,  de estos que hemos metido
		
		if @espejo is null begin
			-- Ponemos el pasillo, la fila y la columna,  de estos que hemos metido
			update #ubi set pasillo=u.pasillo,fila=u.fila,columna=u.columna from ubicaciones as u  inner join #ubi as t
				on u.almacen=t.almacen and u.numero=t.producto where u.estado=0 
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end else begin
			-- Ponemos el pasillo, la fila y la columna,  de estos que hemos metido
			update #ubi set pasillo=u.pasillo,fila=u.fila,columna=u.columna from ubicaciones as u  inner join #ubi as t
				on u.empresa=t.empresa and u.almacen=t.almacen and u.numero=t.producto where u.estado=0 
			if @@error != 0 begin
				raiserror('No se ha podido determinar la ubicación',11,1)
				rollback
				return -1
			end
		end
		
		-- a continuacion insertamos las ubicaciones reservadas		
		/* Carlos 08/01/26: quito el where de cantidad < 0 y lo meto al having
		insert into #ubi (empresa,almacen,producto,cantidad,pasillo,fila,columna) 
			select l.empresa,l.almacen,l.producto,sum(u.cantidad),u.pasillo,u.fila,u.columna from linpedidocmp as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>g.productobonificacion
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numerocmp and l.[nºorden]=u.nºordencmp
				where l.empresa=@empresa and l.número=@pedido and l.VistoBueno=1 and   l.estado=1 and l.[fecharecepcion]<=@fecharecepción and l.cantidad<0 and u.nºordencmp  is not null
					 group by l.empresa,l.almacen,l.producto,u.pasillo,u.fila,u.columna  
		*/

		-- a continuacion insertamos las ubicaciones reservadas		
		insert into #ubi (empresa,almacen,producto,cantidad,pasillo,fila,columna) 
			select l.empresa,l.almacen,l.producto,sum(u.cantidad),u.pasillo,u.fila,u.columna from linpedidocmp as l inner join gruposproducto as g
			on l.empresa=g.empresa and l.grupo=g.numero and l.producto<>isnull(g.productobonificacion,'') -- Carlos 14/10/26: idem
			left join ubicacionesreservadas as u
			on l.empresa=u.empresa and l.numero=u.numerocmp and l.[nºorden]=u.nºordencmp
				where l.empresa=@empresa and l.número=@pedido and l.VistoBueno=1 and l.estado=1 and l.[fecharecepcion]<=@fecharecepción and u.nºordencmp is not null
					 group by l.empresa,l.almacen,l.producto,u.pasillo,u.fila,u.columna
					 having sum(l.cantidad) < 0

		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end	
		
			
		-- De los que se han quedado sin ubicacion, tendriamos que buscar si estan pendientes de ubicar, y si asi fuera, lo tendriamos que ir cogiendo de alli
		
		declare @Producto as char(15)
		declare @Cantidad as integer
		declare @Almacen as char(3)
		
	
		declare @NºOrdenUbicacion as integer
		declare @CantidadUbicacion as integer
		declare @CantidadPendiente as integer
		declare crsUbi cursor local fast_forward for select producto,cantidad,Almacen from #Ubi  where pasillo is null
		if @@Error!=0 begin
			rollback transaction
			raiserror ('No se puede determinar la ubicación',16,1)
			return -1
		end
		open crsUbi
		if @@Error!=0 begin
			rollback transaction
			raiserror ('No se puede determinar la ubicación',16,1)
			return -1
		end
		fetch next from crsUbi into @producto,@cantidad,@almacen
		while @@fetch_status=0 begin
			set @CantidadPendiente=-@cantidad 
	    	
			-- mientras que la cantidad pendiente de asignar sea mayor de cero 
			-- tendremos que hacer esto
			while @CantidadPendiente>0 begin
			
				-- Ponemos sólo las de estado 2
	
				set @NºOrdenUbicacion=0
				select  top 1 @NºOrdenUbicacion= nºorden,@cantidadubicacion=cantidad from ubicaciones where almacen=@almacen and numero=@producto and (empresa=@empresa or empresa=@espejo) and  estado=2 order by fechacreacion asc
				if @@Error!=0 begin
					rollback transaction
					raiserror ('No se puede determinar la ubicación',16,1)
					return -1
				end
				
				if @NºordenUbicacion is not null and @NºordenUbicacion!=0 begin
				
					-- puden pasar 3 opciones
					if @cantidadPendiente=@CantidadUbicacion begin
						-- es que vamos a dar toda esta ubicacion, por lo que modificamos el estado
						--select * from ubicaciones where nºorden=@nºordenUbicacion
						update ubicaciones set cantidad=-cantidad,estado=-1,AlbaranCmp=@UltNumAlbaran + 1,PedidoCmp=@Pedido where nºorden=@nºordenUbicacion
						
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación %d',16,1,@nºordenUbicacion)
							return -1
						End	
						
					end else if @CantidadPendiente<@CantidadUbicacion begin
						-- es que vamos a dar menos de la ubicacion, por lo que modificamos la cantidad en la que estamos y hacemos el insert
						update ubicaciones set cantidad=cantidad-@CantidadPendiente where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End	
						
						-- una vez hecho el update tenemos que insertar por la cantidad pendiente	
					
						insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,estado,AlbaranCmp,PedidoCmp, Usuario)
							select empresa,almacen,numero,-@cantidadPendiente,pasillo,fila,columna,-1,@UltNumAlbaran + 1,@Pedido, @Usuario
							from ubicaciones where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación',16,1)
							return -1
						End
							
					end else if @CantidadPendiente>@CantidadUbicacion begin
						-- es que vamos a dar todo, pero todavia quedara algo 
						
						update ubicaciones set cantidad=-cantidad,estado=-1,AlbaranCmp=@UltNumAlbaran + 1,PedidoCmp=@Pedido where nºorden=@nºordenUbicacion
					
						if @@Error!=0 begin
							rollback transaction
							raiserror('No se puede determinar la ubicación %i',16,1, @nºordenUbicacion)
							return -1
						End		
						
					end
							
				
					-- actualizamos la variable cantidad pendiente		
					set @cantidadPendiente=@cantidadpendiente-@cantidadubicacion
				end else begin
					set @CantidadPendiente=0
				end 	
			end
		fetch next from crsUbi into @producto,@cantidad,@almacen
		end
	close crsUbi
	deallocate crsUbi
		-- tendremos que dividir la linea de ubicacion en dos
		-- primer restamos en la que este
		update ubicaciones set cantidad=u.cantidad+(l.cantidad) from ubicaciones as u inner join #Ubi as l
			on u.empresa=l.empresa and u.almacen=l.almacen and u.numero=l.producto and u.pasillo=l.pasillo and u.fila=l.fila and u.columna=l.columna
			where u.estado=0 and u.pedidocmp is null 
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
		-- a continuacion insertamos y lo insertamos con estado -1
		insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidocmp,albarancmp,estado,NºOrdenCmp, Usuario)
			select l.empresa,l.almacen,l.producto,l.cantidad,u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nºorden], @Usuario 
			from ubicaciones as u inner join linpedidocmp as l
			on u.empresa=l.empresa and u.número=l.producto and u.almacen=l.almacen
			left join ubicacionesreservadas as ur 
			on l.empresa=ur.empresa and l.numero=ur.numerocmp and  l.[nºorden]=ur.[nºordencmp]
			where u.estado=0 and u.pedidocmp is null  and  l.empresa=@empresa and l.VistoBueno=1 and   l.número = @Pedido and l.estado = 1 and l.[fecharecepcion]<=@fecharecepción and l.cantidad<0 and ur.nºordencmp is null
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
		
		-- a continuacion las reservadas
		insert into ubicaciones (empresa,almacen,numero,cantidad,pasillo,fila,columna,pedidocmp,albarancmp,estado,NºOrdenCmp, Usuario)
			select l.empresa,l.almacen,l.producto, u.cantidad,u.pasillo,u.fila,u.columna,l.numero,@UltNumAlbarán+1,-1,l.[nºorden], @Usuario
			from ubicacionesreservadas as u inner join linpedidocmp as l
			on u.empresa=l.empresa and u.numerocmp=l.numero and u.nºordencmp=l.[nºorden]
			where   l.empresa=@empresa and  l.número = @Pedido and l.VistoBueno=1 and l.estado = 1 and l.[fecharecepcion]<=@fecharecepción and l.cantidad<0 and u.nºordencmp is not null
	
		if @@error != 0 begin
			raiserror('No se ha podido determinar la ubicación',11,1)
			rollback
			return -1
		end
	end
	
	-- de los que sean entradas (es decir que cantidad - recoger sea <0) tendremos que hacer el insert pero sin una ubicacion
	-- ya que es una entrada en el almacen y lo que queremos es que se le de una ubicacion con ariadna
	-- lo insertamos con estado -2
	-- David Sanchez . Lo insertamos con estado 2 ya que esta ya en stock,
	insert into ubicaciones (empresa,almacen,numero,cantidad,pedidocmp,albarancmp,estado,nºordencmp, Usuario)
		select l.empresa,l.almacen,l.producto,l.cantidad,l.numero,@UltNumAlbarán+1,2,l.[nºorden], @Usuario
		from linpedidocmp as l inner join productos as p on l.empresa=p.empresa and l.producto=p.numero
			where l.empresa=@empresa and l.VistoBueno=1  and l.número = @Pedido and l.estado = 1 and l.[fecharecepcion]<=@fecharecepción and 
				l.cantidad>0 and p.ubicar=1
	if @@error != 0 begin
		raiserror('No se ha podido determinar la ubicación',11,1)
		rollback
		return -1
	end
	-- borramos las ubicaciones que el estado sea cero y la cantidad sea cero tambien
		delete ubicaciones where estado=0 and cantidad=0
		if @@error != 0 begin
			raiserror('No se han podido insertar los productos en la tabla ubicaciones',11,1)
			rollback
			return -1
		end
end  --> Cierra a lo de si el almacen tiene controles stock
insert into CabAlbaránCmp (empresa,número,fecha,nºproveedor,contacto,PeriodoFacturación,origen, Usuario) values (@empresa,@UltNumAlbarán + 1, @fecha,@NumProveedor,@Contacto,@PeriodoFacturación,@Origen, @Usuario)
if @@error != 0 begin
	raiserror('No se ha podido crear la cabecera de albarán',11,1)
	rollback
	return -1
end

-- Insertamos los Alquileres con el nº de serie
-- Carlos 29/08/12
if (select count(*) from linpedidoCmp as l inner join ProductosAlquiler as p 
on l.Empresa = p.Empresa and l.Producto=p.Número 
where l.empresa=@empresa and l.número = @pedido and l.estado = 1 and l.tipolínea = 1 and l.fecharecepción<=@fechaRecepción and l.VistoBueno=1 and (l.NumSerie is null or RTRIM(l.NumSerie)='')) > 0 begin
	raiserror('Los productos que se pueden alquilar deben llevar número de serie',11,1)
	rollback
	return -1
end 
insert into CabAlquileres (Empresa, Producto, NumeroSerie, NombreProducto, Familia, Usuario)
select l.Empresa, p.Número, l.NumSerie, r.Nombre, r.Familia, @Usuario 
from linpedidoCmp as l inner join ProductosAlquiler as p 
--on l.Empresa = p.Empresa and l.Producto=p.Número --Carlos 21/03/14: Quito porque todos los ProductosAlquiler están en la empresa 1
on l.Producto=p.Número 
inner join Productos as r
on p.Empresa = r.Empresa and p.Número=r.Número
where l.empresa=@empresa and l.número = @pedido and l.estado = 1 and l.tipolínea = 1 and l.fecharecepción<=@fechaRecepción and l.VistoBueno=1
if @@error != 0 begin
	raiserror('No se ha podido crear el alquiler',11,1)
	rollback
	return -1
end



declare @Devuelve as int -- para saber qué me devuelve el prdExtrProducto
-- David Sanchez Comprobamos que haya lineas de producto
if (select count(*) from linpedidoCmp  where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and fecharecepción<=@fechaRecepción and VistoBueno=1) >= 1 begin
	insert into PreExtrProducto (empresa, diario, número, fecha, nºproveedor, contactoproveedor, albarán, texto, almacén,grupo,cantidad,importe,delegación,[forma venta],[asiento automático],linpedido,NºPedido, Usuario) 
		select @Empresa,'_ALBCOMP',producto,@fecha,nºproveedor,@contacto,@ultnumalbarán + 1,texto,almacén,grupo,cantidad,baseimponible,delegación,formaventa,1,nºorden,Número, @Usuario 
		from LinPedidoCmp where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 1 and fecharecepción<=@fechaRecepción and VistoBueno=1
	if @@error != 0 begin
		raiserror('No se ha podido crear el pre-extracto del producto',11,1)
		rollback
		return -1
	end
	exec @Devuelve = prdExtrProducto @empresa,"_ALBCOMP"
	if @Devuelve < 0 begin
		raiserror('No se han podido crear las líneas del extracto de producto',11,1)
		rollback
		return -1
	end
end
/*
David Sánchez.... 24/05/07 ... lo quitamos de aqui y lo ponemos todo al hacer la factura. 

-- David Sánchez López 28/07/03.. Insertamos las lineas de tipo 3 (inmovilizado) en el extracto de inmovilizado
if (select count(*) from linpedidoCmp  where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 3 and fecharecepción<=@fechaRecepción and VistoBueno=1) >= 1 begin
	-- Tipo de apunte 1 porque es un albaran
	insert into PreExtractoinmovilizado (empresa,TipoApunte,número,grupo,concepto,fecha,[nºdocumento],importe,delegación,centrocoste,linpedido) 
		select @Empresa,1,producto,grupo,texto,@fecha,@ultnumalbarán + 1,baseimponible,delegación,centrocoste,[nºorden] from LinPedidoCmp where empresa=@empresa and número = @pedido and estado = 1 and tipolínea = 3 and fecharecepción<=@fechaRecepción and VistoBueno=1
	if @@error != 0 begin
		raiserror('No se ha podido crear el pre-extracto del inmovilizado',11,1)
		rollback
		return -1
	end
	set @devuelve=0
	exec @Devuelve = prdExtractoInmovilizado @empresa
	if @Devuelve < 0 begin
		raiserror('No se han podido crear las líneas del extracto de inmovilizado',11,1)
		rollback
		return -1
	end
end
*/
-- David Sanchez 16/07/03.. Le ponemos el precio tarifa
update linpedidocmp set preciotarifa =pvp from productos inner join linpedidocmp on productos.empresa=linpedidocmp.empresa and productos.número=linpedidocmp.producto where linpedidocmp.empresa=@empresa and linpedidocmp.número = @pedido and linpedidocmp.estado = 1 and fechaRecepción<=@FechaRecepción and VistoBueno=1
if @@error != 0 begin
	raiserror('No se ha podido actualizar el precio de tarifa de las lineas',11,1)
	rollback
	return -1
end
update LinPedidoCmp set nºalbarán = @UltNumAlbarán + 1,fechaalbarán = @fecha,estado = 2 where empresa=@empresa and número = @pedido and estado = 1 and fechaRecepción<=@FechaRecepción and VistoBueno=1
if @@error != 0 begin
	raiserror('No se han podido actualizar las líneas del pedido',11,1)
	rollback
	return -1
end
--------- DAVID SANCHEZ LOPEZ 14/05/03--------- PONEMOS EL CODIGO NECESARIO PARA QUE CONTABILICE
---- Esto se pone solo si contabiliza ------
if @Contabiliza!=0 BEGIN  -- --------/*************************  HACEMOS TODO EL CODIGO ********************************/------------------------
declare @CuentaProv as char(10) -- para guardar la cuenta del proveedor
declare @NumAlbaran as int -- para guardar el numero del albaran
-- David Sanchez. Buscamos la delegacion de la cabecera
declare @NumLineas as int
declare @DelegaciónCab as char(3)
declare @FormaVentaCab as char(3)
set @NumLineas = (select count(distinct [Delegación]) from linpedidocmp where  empresa=@empresa and número = @Pedido and estado = 2 and fecharecepción<=@fecharecepción and VistoBueno=1)
if @NumLineas = 1 begin
	select @DelegaciónCab = Delegación from LinPedidoCmp where  empresa=@empresa and número = @Pedido and estado = 2 and fecharecepción<=@fecharecepción and VistoBueno=1
end else begin
	select @DelegaciónCab = DelegaciónVarios from empresas where número = @empresa
end
set @NumLineas = (select count(distinct [formaventa]) from linpedidocmp where   empresa=@empresa and número = @Pedido and estado = 2 and fecharecepción<=@fecharecepción and VistoBueno=1)
if @NumLineas = 1 begin
	select @FormaVentaCab = [FormaVenta] from LinPedidoCmp where  empresa=@empresa and número = @Pedido and estado = 2 and fecharecepción<=@fecharecepción and VistoBueno=1
end else begin
	select @FormaVentaCab = FormaventaVarios from empresas where número = @empresa
end
set @NumAlbaran=@UltNumAlbarán + 1 -- Este es el numero de albaran que le hemos dado
-- Buscamos la cuenta del proveedor
set @cuentaProv=(select CtaAlbaranes from gruposproveedor as g inner join proveedores as p on g.empresa=p.empresa and g.número=p.grupo where p.empresa=@empresa and p.número=@numproveedor and p.contacto=@contacto)
if @@error != 0 begin
	raiserror('No se han podido encontrar la cuenta del proveedor',11,1)
	rollback
	close crsContabProd
	deallocate crsContabProd
	return(-1)
end
if @cuentaProv is not null or ltrim(rtrim(@CuentaProv) )!=''  begin --****** EMPIEZA SI LA CUENTA NO ES NULA
-- Crear Contabilidad
declare @TotalProveedor money,@BaseImponible money,@Delegación char(3),@FormaVenta char (3),@CtaCompras char(10),@CtaAbonosCmp char(10),@NumAsiento int,
	@ImporteBruto money
set @NumAsiento = 1
-- 600
declare crsContabProd cursor fast_forward for select CtaCompras,CtaAbonosCmp,sum(round(ImporteBruto,2)) as ImporteBruto,Delegación,formaventa from vstContabilizarAlbaranCmp where empresa = @Empresa and NumAlbaran = @NumAlbaran and CtaCompras is not null group by ctaCompras,ctaabonosCmp,delegación,formaVenta
open crsContabProd
fetch next from crsContabProd into @CtaCompras,@CtaAbonosCmp,@ImporteBruto,@delegación,@formaventa
while @@fetch_status = 0 begin
	if @ImporteBruto < 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaAbonosCmp,1,'Albarán  '+ltrim(rtrim(str(@NumAlbaran))),-@ImporteBruto,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegación,@formaventa, @Origen, @Usuario)
	if @ImporteBruto > 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaCompras,1,'Albarán '+ltrim(rtrim(str(@NumAlbaran))),@ImporteBruto,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegación,@formaventa, @Origen, @Usuario)
	if @@error != 0 begin
		raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
		rollback
		close crsContabProd
		deallocate crsContabProd
		return(-1)
	end
	fetch next from crsContabProd into @CtaCompras,@CtaAbonosCmp,@ImporteBruto,@delegación,@formaventa
end -- while @@fetch_status = 0
close crsContabProd
deallocate crsContabProd
-- 765 Descuentos
declare @ImpDtoProveedor money,@ImpDtoProducto money,@ImpDtoCmp money,@ImpDtoPP money
	-- Calculo descuentos en cadena
-- Carlos 31/03/21 quitamos el importe descuento de producto, porque lo calcula abajo
--select @ImpDtoProducto = sum(round(Bruto*DescuentoProducto,2)) from linpedidoCmp where (tipolinea>3 or tipolinea<2) and  empresa=@empresa and NºAlbarán = @NumAlbaran -- El de producto lo calculo para hallar el resto, pero luego lo recalculo grupo a grupo [CARLOS] 22/08/02
select @ImpDtoProducto = 0
select @ImpDtoProveedor = sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoProveedor)))*100/100,2)) from linpedidoCmp where (tipolinea>3 or tipolinea<2) and  empresa=@empresa and NºAlbarán = @NumAlbaran
select @ImpDtoCmp = sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoProveedor)) * (1-(descuento))) *100/100,2)) from linpedidoCmp where (tipolinea>3 or tipolinea<2) and  empresa=@empresa and NºAlbarán = @NumAlbaran
--select @ImpDtoPP= sum(round(bruto*(1-(1-(descuentoProducto)) * (1-(descuentoProveedor)) * (1-(descuento)) * (1-(descuentopp))) * 100/100,2)) from linpedidoCmp where (tipolinea>3 or tipolinea<2) and  empresa=@empresa and NºAlbarán = @NumAlbaran
select @ImpDtoPP= sum(round(bruto * descuentopp / 100,2)) from linpedidoCmp where (tipolinea>3 or tipolinea<2) and  empresa=@empresa and NºAlbarán = @NumAlbaran
/*
set @ImpDtoPP = @ImpDtoPP - @ImpDtoCmp
set @ImpDtoCmp = @ImpDtoCmp - @ImpDtoProducto
set @ImpDtoProveedor = @ImpDtoProveedor - @ImpDtoProducto
*/
set @ImpDtoProveedor=round(@ImpDtoProveedor,2)
set @ImpDtoProducto = round (@ImpDtoProducto, 2)
set @ImpDtoCmp=round(@impdtoCmp,2)
set @ImpDtoPP=round(@impdtopp,2)
	-- Inserto líneas en PreContabildad
	--select sum(ImpDtoProducto),CtaDescuentosCmp,Delegación,FormaVenta from vstContabilizarAlbaranCmp where empresa = @Empresa and NumAlbaran = @NumAlbaran group by CtaDescuentosCmp,Delegación,FormaVenta
declare crsDtoProducto cursor fast_forward for select sum(ImpDtoProducto),CtaDescuentosCmp,Delegación,FormaVenta from vstContabilizarAlbaranCmp where empresa = @Empresa and NumAlbaran = @NumAlbaran group by CtaDescuentosCmp,Delegación,FormaVenta
open crsDtoProducto
fetch next from crsDtoProducto into @ImpDtoProducto,@CtaDtoProductoCmp,@Delegación,@FormaVenta
while @@fetch_status = 0 begin
	--set @ImpDtoPP = @ImpDtoPP - @ImpDtoCmp
	set @ImpDtoCmp = @ImpDtoCmp - @ImpDtoProducto
	set @ImpDtoProveedor = @ImpDtoProveedor - @ImpDtoProducto
	if @ImpDtoProducto < 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaDtoProductoCmp,1,'Descuento Producto Albarán '+ltrim(rtrim(str(@NumAlbaran))),-@ImpDtoProducto,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegación,@formaventa, @Origen, @Usuario)
	if @ImpDtoProducto > 0
		insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
			values (@Empresa,1,@CtaDtoProductoCmp,1,'Descuento Producto Albarán '+ltrim(rtrim(str(@NumAlbaran))),@ImpDtoProducto,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegación,@formaventa, @Origen, @Usuario)
	if @@error != 0 begin
		raiserror('No se han podido crear los asientos de descuento Producto en PreContabilidad',11,1)
		rollback
		close crsDtoProducto
		deallocate crsDtoProducto
		return(-1)
	end
	fetch next from crsDtoProducto into @ImpDtoProducto,@CtaDtoProductoCmp,@Delegación,@FormaVenta
end
close crsDtoProducto
deallocate crsDtoProducto
if @ImpDtoProveedor < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoProveedor,1,'Descuento Proveedor Albarán '+ltrim(rtrim(str(@NumAlbaran))),-@ImpDtoProveedor,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @ImpDtoProveedor > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoProveedor,1,'Descuento Proveedor Albarán '+ltrim(rtrim(str(@NumAlbaran))),@ImpDtoProveedor,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @@error != 0 begin
	raiserror('No se han podido actualizar los descuentos del proveedor',11,1)
	rollback
	return(-1)
end
if @ImpDtoCmp < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDescuentoCmp,1,'Descuento Compra Albarán '+ltrim(rtrim(str(@NumAlbaran))),-@ImpDtoCmp,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @ImpDtoCmp > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDescuentoCmp,1,'Descuento Compra Albarán '+ltrim(rtrim(str(@NumAlbaran))),@ImpDtoCmp,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @@error != 0 begin
	raiserror('No se han podido actualizar los descuentos de compra',11,1)
	rollback
	return(-1)
end
if @ImpDtoPP < 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoPPCmp,1,'Descuento Pronto Pago Albarán '+ltrim(rtrim(str(@NumAlbaran))),-@ImpDtoPP,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @ImpDtoPP > 0
	insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,1,@CtaDtoPPCmp,1,'Descuento Pronto Pago Albarán '+ltrim(rtrim(str(@NumAlbaran))),@ImpDtoPP,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@delegaciónCab,@formaventaCab, @Origen, @Usuario)
if @@error != 0 begin
	raiserror('No se han podido actualizar los descuentos por pronto pago',11,1)
	rollback
	return(-1)
end
set @TotalProveedor=0 -- Tenemos que hacer un select sum de las bases imponibles mirar a ver si se puede hacer de la vista
select  @TotalProveedor=sum(BaseImponible) from vstContabilizarAlbaranCmp where empresa = @Empresa and NumAlbaran = @NumAlbaran 
-- 400 
declare @TotalProveedorCuadre as money -- se usa sólo para cuadrar que el total del proveedor no se descuadre en más de 0.02 de lo real --> Carlos 19/11/02
select @TotalProveedorCuadre = sum(round(TotalAgrupado,2)) from (select sum(BaseImponible) as TotalAgrupado from vstContabilizarAlbaranCmp where NumAlbaran = @NumAlbaran) as Resultado
if abs(@TotalProveedor - @TotalProveedorCuadre) > 0.02 begin -- sólo permitimos 0.02 euros por factura de descuadre entre contabilidad y lineas
	raiserror('Se ha producido un descuadre. Avise al Dpto. de Informática', 11, 1)
	rollback
	return (-3)
end
if @TotalProveedor < 0 
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen, Usuario)
		values (@Empresa,0,1,@CuentaProv,'Albarán '+ltrim(rtrim(str(@NumAlbaran))),-@TotalProveedor,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@DelegaciónCab,@FormaVentaCab, @Origen, @Usuario)
if @TotalProveedor > 0  
	insert into PreContabilidad (empresa,tipoapunte,tipocuenta,[nº cuenta],concepto,haber,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa,Origen, Usuario)
		values (@Empresa,0,1,@CuentaProv,'Albarán '+ltrim(rtrim(str(@NumAlbaran))),@TotalProveedor,@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,@DelegaciónCab,@FormaVentaCab, @Origen, @Usuario)
--set @OrigenLiq = @@identity
if @@error != 0 begin
	raiserror('No se han podido crear los asientos en PreContabilidad',11,1)
	rollback
	return(-1)
end

/*
-- Carlos 31/03/21 comento esto
-- Crear TipoLínea = 2 "Cuenta Contable"--> Carlos 20/11/02
insert into PreContabilidad (empresa,tipocuenta,[nº cuenta],tipoapunte,concepto,debe,fecha,[nº documento],asiento,diario,[asiento automático],delegación,formaventa, Origen,centrocoste,departamento)
	select @Empresa,1,Producto,1,Texto,round(BaseImponible,2),@FechaSinHora,@NumAlbaran,@NumAsiento,'_ALBCMP',1,delegación,formaventa, @Origen,centrocoste,departamento from linPedidoCmp where número = @pedido and estado = 2 and [NºAlbarán]= @NumAlbaran and empresa=@empresa and tipoLínea = 2 and baseimponible<>0
*/
/*
commit
return 2
*/



-- Contabilizar
if(select count(*) from precontabilidad where empresa=@empresa and diario='_ALBCMP')>0 begin
declare @UltAsiento int
/*
--if system_user='NUEVAVISION\Carlos' begin
if system_user='sa' begin
	--update precontabilidad set debe = debe-.02 where diario = '_ALBCMP' and [nº cuenta] = '70000199'
--if @empresa = '4' 
--  update precontabilidad set debe = debe-.03 where diario = '_fravta' and [nº cuenta] = '70000108'-- and concepto like 'Descuento Venta Factura%'
  select * from precontabilidad where diario='_ALBCMP'
  select sum(debe-haber) from precontabilidad where diario='_ALBCMP'
end
*/
exec @UltAsiento = prdContabilizar @Empresa, '_ALBCMP', @Usuario
if (@@error !=0) or (@UltAsiento<0)  begin
	raiserror('Error al contabilizar',11,1)
	rollback
	return(@ultasiento)
end
-- tendremos que actualizar con la cabecera de albaran con el numero de asiento
update cabalbarancmp set asiento=@ultasiento where empresa=@empresa and número=@NumAlbaran
end -- cierra al select count
END --******************* TERMINA AL IF DE SI LA CUENTA ES NULA
END --....****************** TERMINA AL IF DE SI CONTABILIZA ES DISTINTO DE 0
--David Sanchez Lopez... 04/10/04
-- Tenemos que poner el campo rotura de stock proveedor de la tabla productos a 0 de los productos que el campo este a -1
-- y que hayan entrado en este albaran
update productos set roturastockproveedor=0 from productos as p inner join linpedidocmp as l
	on p.empresa=l.empresa and p.numero=l.producto
	where l.empresa=@empresa and l.nºalbaran=@UltNumAlbarán + 1 and l.cantidad>0 and p.roturastockproveedor=1
if @@error != 0 begin
	raiserror('No se ha podido modificar la rotura de stock en la ficha de productos',11,1)
	rollback
	return -1
end
-- borramos por si hay algo en ubicaciones reservadas
delete ubicacionesreservadas from ubicacionesreservadas as u inner join linpedidocmp as l
	on u.empresa=l.empresa and u.numerocmp=l.numero and u.nºordencmp=l.[nºorden]
	where l.empresa=@empresa and l.numero=@pedido and l.[nºalbaran]= @UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido borrar de ubicaciones reservadas',11,1)
	rollback
	return -1
end
-- David Sanchez Lopez.. 05/07/04
-- Ponemos el numero de empresaAlbaranVta en la tabla ubicaciones.
-- No lo podemos poner antes porque como esta relacionado, no se puede poner hasta que no se crea el albaran
update ubicaciones set empresaAlbaranCmp=@empresa where AlbaránCmp=@UltNumAlbarán + 1
if @@error != 0 begin
	raiserror('No se ha podido actualizar la empresa del albarán en la tabla ubicaciones',11,1)
	rollback
	return -1
end
-- David Sánchez López... 18/02/05
-- Borramos de la tabla Entrada de pedidos lo que haya de este proveedor
--03/03/05
-- Lo borramos sólo en el caso de que el albaran sea completo, para ello buscamos que no exista
-- nada de este numero de pedido en estado=1 y enviado=1
if (select count(*) from linpedidocmp where empresa=@empresa and numero=@Pedido and enviado=1 and estado=1)=0 begin
if @@error != 0 begin
		raiserror('No se ha podido actualizar la entrada de pedidos',11,1)
		rollback
		return -1
end
	delete EntradaDePedidos where (empresa=@empresa or empresa=@Espejo)  and proveedor=@NumProveedor and contacto=@Contacto
	if @@error != 0 begin
		raiserror('No se ha podido actualizar la entrada de pedidos',11,1)
		rollback
		return -1
	end
end
-- David Sanchez Lopez...  01/06/06... Los productos que compremos y que tengan el estado con el campo estadoAlComprar
-- que no sea nulo, tendremos que actualizarlos por el campo que me diga.
update productos set estado=e.estadoalcomprar
 from productos as p inner join linpedidocmp as l on
	p.empresa=l.empresa and p.numero=l.producto
	inner join estadosproducto as e
	on e.empresa=p.empresa and e.numero=p.estado
	where l.empresa=@empresa and  l.nºalbaran=@UltNumAlbarán + 1 and e.estadoalcomprar is not null
if @@error != 0 begin
	raiserror('No se ha podido actualizar la entrada de pedidos',11,1)
	rollback
	return -1
end

-- Carlos 08/02/10
-- Comprobamos que todas las ubicaciones estén cuadradas
declare @Retorno as char(15)
exec @retorno = prdQuedaDescuadradaUbicación @Empresa,@Pedido,2

if @retorno <0 begin
		rollback
		return -1
end


commit transaction
return @UltNumAlbarán + 1
-- FIN TRANSACCIÓN

GO
SET ANSI_NULLS ON
GO
SET QUOTED_IDENTIFIER ON
GO

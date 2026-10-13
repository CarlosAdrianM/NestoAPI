/*
NestoAPI#606 (kits, 13/10/26): el informe «Kits que se pueden montar o desmontar» (prdInformeKitsQueSePuedenMontar,
api/Informes/KitsQueSePuedenMontar/Pdf y el aviso del popup del picking de Nesto) dice cuántos KITS hay que desmontar,
con las mismas cuentas que la fecha de entrega a la agencia (RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete).

Ejecutar como sa en NV. Es un ALTER: conserva los permisos que ya tiene el procedimiento (no hacen falta GRANT).

La parte de MONTAR no cambia. La parte de DESMONTAR se reescribe:
 1. «Cantidad» era las UNIDADES que faltaban del componente, no los kits (928288 habría dicho «Desmontar 43191 × 6»,
    seis cajas de 70, cuando basta una), y con ROUND en vez de redondear hacia arriba. Ahora son kits, hacia arriba.
 2. Lo que deben del propio kit los demás pedidos se restaba mal (se cruzaba por el componente, no por el kit): podía
    proponer desmontar un kit ya vendido. Ahora: kits libres = ubicaciones libres del kit (estado 0/2) en el almacén
    − lo que deben del kit las líneas pendientes (estado -1..1) del almacén sin picking, de cualquier fecha.
 3. Lo que faltaba del componente sumaba las líneas de todos los almacenes contra el stock de uno. Ahora solo las del
    almacén del informe.
 4. Si varios kits llevaban el componente, a cada uno se le proponía todo lo que faltaba. Ahora se reparte: primero lo
    que sobró de un kit ya desmontado (al desmontar salen todos sus componentes), luego el kit que más unidades lleva
    del componente (menos kits que desmontar), a igualdad por número de kit.
 5. No se vaciaban #kits ni #kitsPuente entre montar y desmontar (los restos de la parte de montar se colaban).
Igual que antes: solo cuentan las líneas sin picking (el informe se saca antes del picking), con la fecha de entrega
hasta @Fecha, las rutas de @FiltroRutas y, si hay FechaPicking en Empresas, modificadas hasta entonces.
Salida igual que antes: tipo ('m' montar / 'D' desmontar), kit, nombre, CodigoBarras, CantidadAMontar, empresa.
*/
ALTER PROCEDURE [dbo].[prdInformeKitsQueSePuedenMontar] @Empresa as char(3),@Fecha as char(8),@Almacen as char(3),@FiltroRutas as char(500)  AS

/*
declare @Empresa as char(3)
declare @Fecha as char(8)
declare @Almacen as char(3)
declare @FiltroRutas as char(500)

set @Empresa='1'
set @Fecha='15/02/09'
set @Almacen='ALG'
set @Filtrorutas='(ruta=''01 '' or ruta=''FW '' or ruta=''00 '')'
*/

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#Kits') ) drop table [dbo].[#Kits]
CREATE TABLE [#Kits]
	([Producto] [char] (15) not null,
	 [Stock] [int] not null default(0),
         [Cantidad] [int] not null default (0))
             ON [PRIMARY]

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#KitsPuente') ) drop table [dbo].[#KitsPuente]
CREATE TABLE [#KitsPuente]
	([Producto] [char] (15) not null,
	 [Cantidad] [int] not null default (0))
             ON [PRIMARY]

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#KitsProductos') ) drop table [dbo].[#KitsProductos]
CREATE TABLE [#KitsProductos]
	([Tipo] [char] (1),
	[Empresa] [char] (30),
	[Kit] [char] (15) not null,
	 [Producto] [char] (15),
	 [Nombre] [char] (50),
	 [CodigoBarras] nvarchar(50),
	 [CantidadVentaKit] [int] not null default(0),
 	 [CantidadMontajeKit] [int] not null default(0),
         [CantidadNecesariaProducto] [int] not null default(0),
         [CantidadPendienteDelProducto] [int] not null default(0),
         [Stock] [int] not null default (0))
             ON [PRIMARY]





declare @Espejo as char(3)
set @Espejo=(select [iva por defecto] from empresas where numero=@empresa)
declare @fechacomillas as char(10)
set @fechacomillas=''''+@fecha+''''

declare @almacencomillas as char(5)
set @almacencomillas=''''+@almacen+''''

-- Buscamos los kits que entrarian en el picking
-- hay que tener en cuenta la fecha en que se ha cerrado el picking
declare @Sentencia as char(1000)

if (select FechaPicking from empresas where numero=@Empresa) is null begin
	set @sentencia='insert into #kits (producto,cantidad)
		select producto,sum(l.cantidad) from linpedidovta as l
			inner join cabpedidovta as c
			on c.empresa=l.empresa and c.numero=l.numero
			where l.almacen='+ @almacencomillas +' and  l.producto in (select numero from kits where empresa='+@empresa+') and (c.empresa='+@Empresa+' or (c.empresa='+@Espejo+' and c.origen='+@empresa+')) and
			l.estado between -1 and 1 and l.[fecha entrega]<='+@Fechacomillas+' and '+ltrim(rtrim(@filtrorutas))+
			' and l.picking = 0 group by producto'
end else begin
	set @Sentencia='insert into #kits (producto,cantidad)
		select producto,sum(l.cantidad) from linpedidovta as l
			inner join cabpedidovta as c
			on c.empresa=l.empresa and c.numero=l.numero
			where l.almacen='+ @almacencomillas +' and   l.producto in (select numero from kits where empresa='+@empresa+') and (c.empresa='+@Empresa+' or (c.empresa='+@Espejo+' and c.origen='+@empresa+')) and
			l.estado between -1 and 1 and l.[fecha entrega]<='+@FechaComillas+
			' and l.[fecha modificacion]<=(select FechaPicking from empresas where numero='+@Empresa+') and '+ltrim(rtrim(@filtrorutas))+
			' and l.picking = 0 group by producto'

end


-- Carlos 06/02/09
execute (@Sentencia)




-- Buscamos los stock que seran las ubicaciones que no estan reservadas para nada
insert into #KitsPuente (producto,cantidad)
select numero,sum(u.cantidad) from ubicaciones as u inner join #kits as k
	on u.numero=k.producto
	where (u.empresa=@empresa or u.empresa=@espejo) and u.almacen=@Almacen
	and (u.estado=0 or u.estado=2)
	group by numero


update #kits set stock=p.cantidad from #kitspuente as p inner join #kits as k on p.producto=k.producto



-- borramos los que el stock sea mayor o igual a la cantidad
delete #kits where stock>=cantidad

-- Metemos los productos asociados para ver si hay stock de ellos
-- la cantidad de venta sera la cantidad antigua menos el stock, ya que si hay 1 en stock y queremos vender 2, en realidad solo necesito montar 1.



insert into  #kitsproductos (tipo,kit,producto,cantidadventakit,cantidadmontajekit)
select 'm', t.producto,k.numeroasociado,(t.cantidad-t.stock),k.cantidad
 from #kits as t inner join kits as k on k.empresa=@empresa and t.producto=k.numero


-- ponemos la cantidad que se necesita del producto
update #kitsproductos set cantidadnecesariaproducto=cantidadventakit*cantidadmontajekit
-- Ponemos el stock de los productos
delete #kitsPuente

insert into #KitsPuente (producto,cantidad)
select numero,sum(u.cantidad) from ubicaciones as u inner join #kitsProductos as k
	on u.numero=k.producto
	where (u.empresa=@empresa or u.empresa=@espejo) and u.almacen=@Almacen
	and (u.estado=0 or u.estado=2)
	group by numero

update #kitsProductos set stock=p.cantidad from #kitspuente as p inner join #kitsProductos as k on p.producto=k.producto


delete #kitsProductos where stock=0

-- Tenemos que buscar la cantidad pendiente de servir del producto
-- y restarselo al stock, ya que si no dejaremos pendientes los productos en lugar
-- del kit
delete #kitsPuente

insert into #KitsPuente (producto,cantidad)
select l.producto,sum(l.cantidad) from linpedidovta as l inner join #kitsProductos as k
	on l.producto=k.producto
	inner join cabpedidovta as c on c.empresa=l.empresa and c.numero=l.numero
	where (c.empresa=@empresa or (c.empresa=@espejo and c.origen=@empresa)) and l.almacen=@Almacen
	and l.estado between -1 and 1 and l.[fecha entrega]<=@fecha
	group by l.producto

update #kitsProductos set CantidadPendienteDelProducto=p.cantidad from #kitspuente as p inner join #kitsProductos as k on p.producto=k.producto

update #kitsproductos set stock=stock-CantidadPendienteDelProducto

-- Tenemos que ver los que la cantidad de venta del kit sea mayor a 1
-- ya que es posible que aunque no se puedan montar todos, si se pueda montar alguno
-- para ello lo que hacemos es modificar la cantidad de venta del kit, y le ponemos
-- todo lo que se pueda montar, que es el stock dividio entre la cantidad de montaje
-- una vez modificada la cantidad de venta modificamos tambien la cantidad necesaria de producto
update  #kitsproductos set CantidadVentaKit=stock/CantidadMontajeKit,CantidadNecesariaProducto=(stock/CantidadMontajeKit)*CantidadMontajeKit
	 where cantidadventakit>1 and cantidadnecesariaproducto>stock
	and stock/cantidadmontajekit<>0




-- borramos los que el stock sea menor a la cantidad necesario
delete #kitsProductos where stock<cantidadnecesariaproducto

-- Ahora tenemos que ver los kits completos que quedan, eso se puede ver mirando los productos
-- que componen un kits en la tabla kits y mirandolos en la temporal, los que sean iguales seran
-- los que se puedan quedar
-- Si lo hacemos asi con un Union, si los sumamos, los que den cero es que tienen los mismo numero
-- de productos tanto en la tabla kit como en la temporal
delete #kitsProductos where kit not in (
select numero from (
select 'K' as tipo,numero,count(*) as cantidad from (
select k.numero,numeroasociado from kits as k inner join #kitsproductos as t on k.empresa=@empresa and k.numero=t.kit
group by k.numero,numeroasociado
) as a group by numero
union
select 'T',kit,-count(*) from #kitsproductos group by kit
) as a group by numero having sum(cantidad)=0
)


-- ponemos el nombre del kit
update #kitsproductos set nombre=p.Nombre, CodigoBarras = p.CodBarras
from Productos as p inner join #kitsproductos as k
on p.empresa=@empresa and k.kit=p.numero

-- Ponemos el nombre de la empresa
update #KitsProductos set empresa=(select nombre from empresas where numero=@empresa)


-- A PARTIR DE AQUI ES PARA VER LOS KITS QUE SE PUEDEN DESMONTAR
-- NestoAPI#606 (13/10/26): reescrito. Mismas reglas que la fecha de entrega a la agencia
-- (RepartidorStockFechaEntregaAgencia.BolsaKitsAlgete en NestoAPI).

-- (5) Nada de la parte de montar se cuela aquí.
delete #kits
delete #kitsPuente

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DNecesario') ) drop table [dbo].[#DNecesario]
CREATE TABLE [#DNecesario] ([Producto] [char] (15) not null, [Cantidad] [int] not null)

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DFaltan') ) drop table [dbo].[#DFaltan]
CREATE TABLE [#DFaltan] ([Producto] [char] (15) not null primary key, [Faltan] [int] not null)

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DComponentes') ) drop table [dbo].[#DComponentes]
CREATE TABLE [#DComponentes] ([Kit] [char] (15) not null, [Componente] [char] (15) not null, [Cantidad] [int] not null,
	primary key ([Kit], [Componente]))

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DKitsLibres') ) drop table [dbo].[#DKitsLibres]
CREATE TABLE [#DKitsLibres] ([Kit] [char] (15) not null primary key, [Libres] [int] not null)

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DSueltos') ) drop table [dbo].[#DSueltos]
CREATE TABLE [#DSueltos] ([Kit] [char] (15) not null, [Componente] [char] (15) not null, [Cantidad] [int] not null,
	primary key ([Kit], [Componente]))

if exists (select * from tempdb..sysobjects where id = object_id('tempdb..#DDesmontar') ) drop table [dbo].[#DDesmontar]
CREATE TABLE [#DDesmontar] ([Kit] [char] (15) not null primary key, [Cantidad] [int] not null)

-- Lo que piden del componente las líneas del picking (sin picking aún) de ESTE almacén (3).
declare @FechaPicking as datetime
set @FechaPicking=(select FechaPicking from empresas where numero=@Empresa)

declare @SentenciaD as nvarchar(max)
set @SentenciaD=N'insert into #DNecesario (producto,cantidad)
	select l.producto,sum(l.cantidad) from linpedidovta as l
		inner join cabpedidovta as c
		on c.empresa=l.empresa and c.numero=l.numero
		where l.almacen=@Almacen
		and l.producto in (select numeroasociado from kits where empresa=@Empresa and numeroasociado<>numero)
		and (c.empresa=@Empresa or (c.empresa=@Espejo and c.origen=@Empresa))
		and l.estado between -1 and 1 and l.[fecha entrega]<=@Fecha
		and (@FechaPicking is null or l.[fecha modificacion]<=@FechaPicking)
		and l.picking = 0 and '+ltrim(rtrim(@FiltroRutas))+N'
		group by l.producto'

execute sp_executesql @SentenciaD,
	N'@Empresa char(3), @Espejo char(3), @Almacen char(3), @Fecha char(8), @FechaPicking datetime',
	@Empresa=@Empresa, @Espejo=@Espejo, @Almacen=@Almacen, @Fecha=@Fecha, @FechaPicking=@FechaPicking

-- Lo que falta = lo que piden − las ubicaciones libres (estado 0/2) del almacén.
insert into #DFaltan (producto,faltan)
select n.producto, n.cantidad-isnull(s.cantidad,0)
from (select producto,sum(cantidad) as cantidad from #DNecesario group by producto) as n
	left join (select u.numero as producto,sum(u.cantidad) as cantidad from ubicaciones as u
		where (u.empresa=@empresa or u.empresa=@espejo) and u.almacen=@Almacen and (u.estado=0 or u.estado=2)
		and u.numero in (select producto from #DNecesario)
		group by u.numero) as s
	on s.producto=n.producto
where n.cantidad-isnull(s.cantidad,0)>0

-- Todos los componentes de los kits que llevan algo de lo que falta (al desmontar salen todos).
insert into #DComponentes (kit,componente,cantidad)
select k.numero,k.numeroasociado,sum(cast(k.cantidad as int)) from kits as k
	where k.empresa=@empresa and k.numeroasociado<>k.numero
	and k.numero in (select a.numero from kits as a where a.empresa=@empresa and a.numeroasociado in (select producto from #DFaltan))
	-- Fuera los «kits» con materias primas (grupo MTP): lista de materiales de fabricacion, no se desmontan (Carlos 13/10/26)
	and not exists (select 1 from kits as m inner join productos as pm on pm.empresa=m.empresa and pm.numero=m.numeroasociado
		where m.empresa=k.empresa and m.numero=k.numero and pm.grupo='MTP')
	group by k.numero,k.numeroasociado
	having sum(cast(k.cantidad as int))>0

-- (2) Kits libres = ubicaciones libres del kit en el almacén − lo que deben del KIT las líneas pendientes del almacén
-- sin picking (las que ya tienen picking tienen su ubicación reservada, estado 3: no están en las libres).
insert into #DKitsLibres (kit,libres)
select k.kit, isnull(s.cantidad,0)-isnull(p.cantidad,0)
from (select distinct kit from #DComponentes) as k
	left join (select u.numero as kit,sum(u.cantidad) as cantidad from ubicaciones as u
		where (u.empresa=@empresa or u.empresa=@espejo) and u.almacen=@Almacen and (u.estado=0 or u.estado=2)
		and u.numero in (select kit from #DComponentes)
		group by u.numero) as s on s.kit=k.kit
	left join (select l.producto as kit,sum(l.cantidad) as cantidad from linpedidovta as l
		inner join cabpedidovta as c on c.empresa=l.empresa and c.numero=l.numero
		where (c.empresa=@empresa or (c.empresa=@espejo and c.origen=@empresa)) and l.almacen=@Almacen
		and l.estado between -1 and 1 and l.picking = 0
		and l.producto in (select kit from #DComponentes)
		group by l.producto) as p on p.kit=k.kit

-- (4) Reparto de lo que falta entre los kits, producto a producto.
declare @Producto as char(15)
declare @Resto as int
declare @Kit as char(15)
declare @CantidadEnKit as int
declare @Usar as int
declare @Libres as int
declare @ADesmontar as int

declare cProductosD cursor local fast_forward for
	select producto,faltan from #DFaltan order by producto
open cProductosD
fetch next from cProductosD into @Producto,@Resto
while @@fetch_status=0 begin
	-- Primero lo que sobró de un kit ya desmontado para otro producto.
	while @Resto>0 and exists (select * from #DSueltos where componente=@Producto and cantidad>0) begin
		select top 1 @Kit=s.kit, @Usar=s.cantidad
			from #DSueltos as s inner join #DComponentes as c on c.kit=s.kit and c.componente=s.componente
			where s.componente=@Producto and s.cantidad>0
			order by c.cantidad desc, s.kit
		if @Usar>@Resto set @Usar=@Resto
		update #DSueltos set cantidad=cantidad-@Usar where kit=@Kit and componente=@Producto
		set @Resto=@Resto-@Usar
	end

	-- Después desmontando: primero el kit que más unidades lleva del producto (menos kits que desmontar).
	declare cKitsD cursor local fast_forward for
		select kit,cantidad from #DComponentes where componente=@Producto order by cantidad desc, kit
	open cKitsD
	fetch next from cKitsD into @Kit,@CantidadEnKit
	while @@fetch_status=0 and @Resto>0 begin
		set @Libres=isnull((select libres from #DKitsLibres where kit=@Kit),0)
		-- (1) Kits, redondeando hacia arriba.
		set @ADesmontar=(@Resto+@CantidadEnKit-1)/@CantidadEnKit
		if @ADesmontar>@Libres set @ADesmontar=@Libres
		if @ADesmontar>0 begin
			update #DKitsLibres set libres=libres-@ADesmontar where kit=@Kit

			if exists (select * from #DDesmontar where kit=@Kit)
				update #DDesmontar set cantidad=cantidad+@ADesmontar where kit=@Kit
			else
				insert into #DDesmontar (kit,cantidad) values (@Kit,@ADesmontar)

			-- Al desmontar salen todos sus componentes.
			update s set cantidad=s.cantidad+@ADesmontar*c.cantidad
				from #DSueltos as s inner join #DComponentes as c on c.kit=s.kit and c.componente=s.componente
				where c.kit=@Kit
			insert into #DSueltos (kit,componente,cantidad)
				select c.kit,c.componente,@ADesmontar*c.cantidad from #DComponentes as c
				where c.kit=@Kit and not exists (select * from #DSueltos as s where s.kit=c.kit and s.componente=c.componente)

			set @Usar=(select cantidad from #DSueltos where kit=@Kit and componente=@Producto)
			if @Usar>@Resto set @Usar=@Resto
			update #DSueltos set cantidad=cantidad-@Usar where kit=@Kit and componente=@Producto
			set @Resto=@Resto-@Usar
		end
		fetch next from cKitsD into @Kit,@CantidadEnKit
	end
	close cKitsD
	deallocate cKitsD

	fetch next from cProductosD into @Producto,@Resto
end
close cProductosD
deallocate cProductosD

insert into #KitsProductos (tipo,kit,nombre,CodigoBarras,CantidadVentaKit,empresa)
	select 'D', d.kit, p.nombre, p.CodBarras, d.cantidad, (select nombre from empresas where numero=@empresa)
	from #DDesmontar as d
		left join productos as p on p.empresa=@empresa and p.numero=d.kit
	where d.cantidad>0


select tipo, kit,nombre, CodigoBarras, min(cantidadventakit) as CantidadAMontar,empresa
from #kitsproductos
group by tipo,kit,nombre,empresa, CodigoBarras

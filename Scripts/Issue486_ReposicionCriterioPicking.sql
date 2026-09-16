-- NestoAPI#486 (16/09/26): la reposición de tiendas reparte el stock escaso (caso 3, el cursor sobre
-- @pendientes) con el MISMO criterio que el picking: Fecha Modificación y, a igualdad, Nº Orden. Antes
-- iba solo por Nº Orden, y con criterios distintos la reposición podía mover stock que luego no iba al
-- picking. Decisión de Carlos: el criterio bueno es el del picking.
--
-- Cambios respecto a la versión en producción (definición descargada de sys.sql_modules el 16/09/26):
--   1. @pendientes lleva fechaModificacion (de LinPedidoVta.[Fecha Modificación]).
--   2. El cursor ordena por producto, fechaModificacion, numOrden (y selecciona columnas explícitas).
-- Nada más cambia. Ejecutar en SSMS como sa (ALTER). Comprobar después con @debug = 1 sobre un producto
-- con escasez real que el caso 3 reparte como el picking.
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
ALTER PROCEDURE [dbo].[prdRellenarReposicionStock]  @Empresa as char(3),@AlmacenOrigen as char(3),@AlmacenDestino as char(3) AS
-- declaraciones
/*
declare @empresa as char(3) = '1'
declare @almacenOrigen as char(3) = 'REI'
declare @almacenDestino as char(3) = 'ALG'
*/
declare @espejo as char(3) = (select [IVA por defecto] from Empresas where Número = @empresa)
declare @EMPRESA_VISNU as char(3) = '4'
declare @debug as bit = 0
declare @pedidoCongreso as int = 724488 -- Carlos 07/10/16: se usa para ignorar completamente un pedido determinado
declare @almacenCentral as char(3) = 'ALG'
DECLARE @almacenesRepo TABLE (almacen NVARCHAR(50))
INSERT INTO @almacenesRepo (almacen)
VALUES ('ALG'), ('REI'), ('ALC')

-- Carlos 23/04/26: añadimos esta variable para forzar el envío de todo el stock sobrante de una familia concreta
DECLARE @ForzarEnvioFamilia bit = 0
DECLARE @FamiliaForzada char(3) = NULL

/*
if SYSTEM_USER != 'NUEVAVISION\Carlos' begin
	raiserror('Reposición bloqueada por Carlos',11,1)
	return
end
*/
-- Miramos si se puede rellenar. Si hay movimientos de otra repo, no permitimos rellenar para que no se mezclen
----------------------------------------------------
if (select top 1 Empresa from PreExtrProducto where Almacén = @almacenDestino and isnull(NºTraspaso,0) <> 0 and Diario <> 'RepoEscond') is not null begin
	raiserror('No se puede rellenar porque hay una reposición anterior pendiente de contabilizar',11,1)
	return
end

-- Empezamos por rellenar el almacén destino: stock (aquí entran las pendientes de recibir de repos anteriores), stockMax y pendientes (solo las que tienen stockMax, por eficiencia)
declare @reponer as table (
	producto char(15),
	grupo char(3),
	texto char(50),
	stockMaximoOrigen smallint not null default(0),
	stockMinimoOrigen smallint not null default(0), -- Carlos 10/12/25: para cuando el origen es ALG
	stockOrigen smallint not null default(0),
	pendientesOrigen smallint not null default(0),
	stockMaximoDestino smallint not null default(0), -- Carlos 04/09/23: rellenar en tabla aparte, separando por almacén
	stockDestino smallint not null default(0),
	pendientesDestino smallint not null default(0), -- Carlos 04/09/23: rellenar en tabla aparte, separando por almacén
	cantidadReponer smallint,
	caso tinyint
)

/*
AND (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        )
*/

-- Insertamos los que tienen stock máximo
insert into @reponer (producto, stockMaximoDestino)
select Número, StockMáximo from ControlesStock where Empresa = @empresa and Almacén = @almacenDestino and StockMáximo>0

/*
-- Insertamos los que tienen stock máximo
insert into @reponer (producto, stockMaximoDestino)
select Número, StockMáximo from ControlesStock where Empresa = @empresa AND (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        ) and Almacén IN (SELECT almacen FROM @almacenesRepo)
 and StockMáximo>0
*/

-- insertamos los que tienen pendientes y no tienen stock máximo
insert into @reponer (producto)
select Producto from LinPedidoVta
where Empresa in (@empresa, @espejo) and Almacén = @almacenDestino and Estado between -1 and 1 and Producto not in (select producto from @reponer)
and Número <> @pedidoCongreso
group by Producto
/*
-- insertamos los que tienen pendientes y no tienen stock máximo
insert into @reponer (producto)
select Producto from LinPedidoVta
where Empresa in (@empresa, @espejo) AND (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        )  and Almacén IN (SELECT almacen FROM @almacenesRepo)
		and Estado between -1 and 1 and Producto not in (select producto from @reponer)
and Número <> @pedidoCongreso
group by Producto
*/

-- borramos los que sean ficticios
if @debug = 0
	delete @reponer
	from @reponer as r inner join Productos as p
	on p.Empresa = '1' and r.producto = p.Número
	where p.Ficticio = 1 or p.Estado < 0
else
	update @reponer set caso = 6
	from @reponer as r inner join Productos as p
	on p.Empresa = '1' and r.producto = p.Número
	where p.Ficticio = 1 or p.Estado < 0


-- Rellenamos StockDestino
update @reponer set stockDestino = s.Stock
from @reponer as r inner join
(select Número, SUM(cantidad) as Stock from ExtractoProducto where Empresa in (@empresa, @espejo, @EMPRESA_VISNU) and Almacén = @almacenDestino and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
/*
-- Rellenamos StockDestino
update @reponer set stockDestino = s.Stock
from @reponer as r inner join
(select Número, SUM(cantidad) as Stock from ExtractoProducto where Empresa in (@empresa, @espejo, @EMPRESA_VISNU) AND (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        ) and Almacén IN (SELECT almacen FROM @almacenesRepo)
		and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
*/

-- Carlos 30/08/23: modificamos para que se tenga en cuenta desde Reina lo pendiente en Alcobendas y viceversa
-- Rellenamos PendientesDestino
update @reponer set pendientesDestino = s.Cantidad
from @reponer as r inner join
(
	select Producto, isnull(SUM(cantidad),0) as Cantidad
	from LinPedidoVta
	where Empresa in (@empresa, @espejo) and Almacén = @almacenDestino and Estado between -1 and 1 and Producto in (select producto from @reponer)
	and Número <> @pedidoCongreso
	group by Producto
) as s
on r.producto = s.Producto
/*
UPDATE @reponer
SET pendientesDestino = s.Cantidad
FROM @reponer AS r
INNER JOIN
(
    SELECT Producto, ISNULL(SUM(cantidad), 0) AS Cantidad
    FROM LinPedidoVta
    WHERE Empresa IN (@empresa, @espejo)
        AND (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        )
		and Almacén IN (SELECT almacen FROM @almacenesRepo)
        AND Estado BETWEEN -1 AND 1
        AND Producto IN (SELECT producto FROM @reponer)
        AND Número <> @pedidoCongreso
    GROUP BY Producto
) AS s
ON r.producto = s.Producto
*/

-- Sumamos la cantidad pendiente de dar de alta en la repo al stockDestino
update @reponer set stockDestino = stockDestino + s.Cantidad
from @reponer as r inner join
(select Número, SUM(cantidad) as Cantidad from PreExtrProducto where Almacén = @almacenDestino and isnull(NºTraspaso,0) <> 0 and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
/*
-- Sumamos la cantidad pendiente de dar de alta en la repo al stockDestino
update @reponer set stockDestino = stockDestino + s.Cantidad
from @reponer as r inner join
(select Número, SUM(cantidad) as Cantidad from PreExtrProducto where (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        ) and Almacén IN (SELECT almacen FROM @almacenesRepo)
		and isnull(NºTraspaso,0) <> 0 and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
*/

-- Si stock >= (stockMax + Pendientes) borramos la línea
----------------------------------------------------
if @debug = 0
	delete @reponer where stockDestino >= (stockMaximoDestino + pendientesDestino)
else
	update @reponer set caso = 7 where stockDestino >= (stockMaximoDestino + pendientesDestino)

-- En las que quedan calculamos stock del origen
----------------------------------------------------
update @reponer set stockOrigen = s.Stock
from @reponer as r inner join
(select Número, SUM(cantidad) as Stock from ExtractoProducto where Empresa in (@empresa, @espejo, @EMPRESA_VISNU) and Almacén = @almacenOrigen and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número

-- Los que el stock origen sea cero las borramos
----------------------------------------------------
if @debug = 0
	delete @reponer where stockOrigen = 0
else
	update @reponer set caso = 8 where stockOrigen = 0

-- Calculamos en el origen pendientes, stock máximo y sumamos las pendientes de recibir de las repos anterior al stock
-- En las que stockOrigen >= (stockMaxOrigen + PdteOrigen + stockMaxDestino + PdteDestino - stockDestino) calculamos CantidadReposicion y nos olvidamos de ellos
	--> de aquí en adelante ya no entra ninguna de las que tienen cantidadReposicion
----------------------------------------------------
update @reponer set pendientesOrigen = s.Cantidad
from @reponer as r inner join
(
	select Producto, isnull(SUM(cantidad),0) as Cantidad
	from LinPedidoVta
	where Empresa in (@empresa, @espejo) and Almacén = @almacenOrigen and Estado between -1 and 1 and Producto in (select producto from @reponer)
	and Número <> @pedidoCongreso
	group by Producto
) as s
on r.producto = s.Producto

update @reponer set stockOrigen = stockOrigen + s.Cantidad
from @reponer as r inner join
(select Número, SUM(cantidad) as Cantidad from PreExtrProducto where Almacén = @almacenDestino and isnull(NºTraspaso,0) <> 0 and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
/*
update @reponer set stockOrigen = stockOrigen + s.Cantidad
from @reponer as r inner join
(select Número, SUM(cantidad) as Cantidad from PreExtrProducto where (
            (Almacén = @almacenDestino AND @almacenDestino != @almacenCentral) OR
            (Almacén != @almacenOrigen AND @almacenDestino = @almacenCentral)
        ) and Almacén IN (SELECT almacen FROM @almacenesRepo)
		and isnull(NºTraspaso,0) <> 0 and Número in (select producto from @reponer) group by Número) as s
on r.producto = s.Número
*/
-- Caso 5 todos los pendientes están en el origen y son más que el stock de origen -> no reponemos nada
if @debug = 0
	delete @reponer where pendientesDestino = 0 and pendientesOrigen >= stockOrigen and caso is null
else
	update @reponer set cantidadReponer = 0, caso = 5
	where pendientesDestino = 0 and pendientesOrigen >= stockOrigen and caso is null

update @reponer set stockMaximoOrigen = StockMáximo, stockMinimoOrigen = StockMínimo
from @reponer as r inner join ControlesStock as c
on r.producto = c.Número
where Empresa = @empresa and Almacén = @almacenOrigen and StockMáximo>0

-- Carlos 10/12/25: tratamos diferente cuando el origen es ALG porque en ese almacén el StockMáximo y el StockMínimo son diferentes
/*
update @reponer set cantidadReponer = stockMaximoDestino - stockDestino + pendientesDestino, caso = 1 -- Carlos 04/09/23 aquí podría ser que haya que hacer otro update para el resto de almacenes (<>ALG y <>origen). Tened en cuenta el stock asignado en esta línea para no asignarlo dos veces.
where stockOrigen + stockDestino >= (stockMaximoOrigen + pendientesOrigen + stockMaximoDestino + pendientesDestino)
*/
UPDATE @reponer
SET  cantidadReponer = stockMaximoDestino - stockDestino + pendientesDestino,
     caso = 1
WHERE stockOrigen + stockDestino >=
      CASE
          WHEN RTRIM(@AlmacenOrigen) = 'ALG'
               THEN stockMinimoOrigen + pendientesOrigen + stockMaximoDestino + pendientesDestino
          ELSE stockMaximoOrigen + pendientesOrigen + stockMaximoDestino + pendientesDestino
      END;

-- En las que stockOrigen + stockDestino >= pendientesOrigen + pendientesDestino ponemos cantidadReposicion = pendientesDestino - stockDestino + (parte proporcional StockMaxDestino de lo que sobre)
	-- lo que sobra es stockOrigen + stockDestino - pdteOrigen - pdteDestino
	-- la parte proporcional redondea el exceso hacia el origen, para trabajar menos
----------------------------------------------------
/*
update @reponer
set cantidadReponer = FLOOR(
pendientesDestino - stockDestino + (
((stockOrigen + stockDestino - pendientesOrigen - pendientesDestino) * stockMaximoDestino) / (stockMaximoOrigen + stockMaximoDestino)
)
),
caso = 2
where stockOrigen + stockDestino >= pendientesOrigen + pendientesDestino and isnull(cantidadReponer, 0) = 0
*/
update @reponer
set cantidadReponer = pendientesDestino - stockDestino, caso = 2
where stockOrigen + stockDestino >= pendientesOrigen + pendientesDestino and caso is null

-- Los de caso 2 que no sale a reponer nada, dividimos el stock proporcionalmente
/*
update @reponer
set cantidadReponer = FLOOR(
pendientesDestino - stockDestino + (
((stockOrigen + stockDestino - pendientesOrigen - pendientesDestino) * stockMaximoDestino) / (stockMaximoOrigen + stockMaximoDestino)
)
),
caso = 9
where caso = 2 and cantidadReponer <= 0
*/
update @reponer
set cantidadReponer = FLOOR((stockDestino + stockOrigen - pendientesDestino - pendientesOrigen) / 2) - stockDestino + pendientesDestino,
caso = 9
where caso = 2 and cantidadReponer <= 0

update @reponer
set cantidadReponer = stockMaximoDestino - stockDestino - pendientesDestino
where caso = 9 and cantidadReponer > stockMaximoDestino - stockDestino - pendientesDestino

-- Borramos los que la cantidad a reponer sea insignificante comparada con el stock destino (multiplicamos por un índice de compensación arbitrario)
if @debug = 0
	delete @reponer where caso = 9 and cantidadReponer < stockDestino * .5
else
	update @reponer set cantidadReponer=0, caso = 10 where caso = 9 and cantidadReponer < stockDestino * .5

-- Carlos 20/02/26: no reponemos si hay stock en ambos almacenes y no hay pendientes en el destino (evita ping-pong)
if @debug = 0
    delete @reponer
    where caso = 9
    and pendientesDestino = 0
    and stockDestino > 0
    and stockOrigen > 0
else
    update @reponer set cantidadReponer = 0, caso = 10
    where caso = 9
    and pendientesDestino = 0
    and stockDestino > 0
    and stockOrigen > 0

if @debug = 0
	delete @reponer where cantidadReponer <= 0 and (caso = 2 or caso = 9)

-- Caso 4: todas las pendientes son del destino, por lo que no hay que mirar números de orden. Se haría automáticamente con el caso 3, pero lo separo por rendimiento
update @reponer set cantidadReponer = stockOrigen, caso = 4
where stockOrigen > 0 and pendientesOrigen = 0 and pendientesDestino > stockDestino and caso is null
update @reponer set cantidadReponer = pendientesDestino - stockDestino where pendientesDestino - stockDestino < cantidadReponer and caso = 4

-- En las que quedan sin cantidadReposicion es porque no hay suficiente stock para servir todos los pedidos, por lo que hay ver los números de orden.
	-- cantidadReposicion será la suma de pendientes de almacén destino cuyo nº de orden sea cubierto
	-- un pedido grande bloquearía que entregasen pedidos pequeños posteriores.
		-- Insertamos en una tabla los linpedidovta (estado -1 y 1) de los productos que quedan por calcular. Con nº orden, almacen y cantidad es suficiente
		-- Recorremos con un cursor en orden producto, nº orden. Como el cursor será FAST_FORWARD iremos metiendo en una tabla los valores a actualizar.
		-- Actualizamos cantidadReposicion
----------------------------------------------------

declare @pendientes table (
	producto char(15),
	numOrden int,
	almacen char(3),
	cantidad smallint,
	cantidadRepartir smallint,
	fechaModificacion datetime -- NestoAPI#486 (16/09/26): el picking reparte por Fecha Modificación y luego Nº Orden
)

insert into @pendientes (producto, numOrden, almacen, cantidad, fechaModificacion)
select l.Producto, [Nº Orden], l.Almacén, Cantidad, l.[Fecha Modificación]
from LinPedidoVta as l inner join @reponer as r
on l.Producto = r.producto
where Empresa in (@empresa, @espejo) and Almacén in (SELECT almacen FROM @almacenesRepo) and Estado between -1 and 1 and r.caso is null
and l.Número <> @pedidoCongreso

update @pendientes set cantidadRepartir = stockOrigen + stockDestino
from @reponer as r inner join @pendientes as p
on r.producto = p.producto

declare @cursor as cursor
declare @productoCursor char(15)
declare @numOrdenCursor int
declare @almacenCursor char(3)
declare @cantidadCursor smallint
declare @cantidadRepartirCursor smallint
declare @sumaCantidad smallint = 0
declare @productoAnterior char(15)
declare @cantidadReponerCursor smallint = 0

-- NestoAPI#486 (16/09/26): MISMO criterio que el picking (GestorReservasStock: Fecha_Modificación y, a
-- igualdad, Nº Orden). Antes se repartía solo por Nº Orden y la reposición podía traer stock «para» un
-- pedido que luego el picking daba a otro (o dejar en la tienda stock que el picking sí habría servido).
SET @cursor = CURSOR FAST_FORWARD FOR
	select producto, numOrden, almacen, cantidad, cantidadRepartir from @pendientes order by producto, fechaModificacion, numOrden

--select * from @pendientes order by producto, numOrden

OPEN @cursor
FETCH NEXT FROM @cursor INTO @productoCursor, @numOrdenCursor, @almacenCursor, @cantidadCursor, @cantidadRepartirCursor
WHILE @@FETCH_STATUS = 0 BEGIN
	 if @productoCursor <> @productoAnterior and @productoAnterior is not null begin
		update @reponer set cantidadReponer = @cantidadReponerCursor - stockDestino, caso = 3 where producto = @productoAnterior and caso is null
		set @sumaCantidad = 0
		set @cantidadReponerCursor = 0
	 end
	 set @productoAnterior = @productoCursor
	 set @sumaCantidad = @sumaCantidad + @cantidadCursor
	 if @cantidadRepartirCursor >= @sumaCantidad and @almacenDestino = @almacenCursor begin-- Carlos 04/09/23: esto de @almacenDestino = @almacenCursor hay que ver si funciona con ALC
		set @cantidadReponerCursor = @cantidadReponerCursor + @cantidadCursor
	 end else if @cantidadRepartirCursor >= @sumaCantidad - @cantidadCursor and @almacenDestino = @almacenCursor begin
		set @cantidadReponerCursor = @cantidadCursor + (@cantidadRepartirCursor - @sumaCantidad)
	 end
    FETCH NEXT FROM @cursor INTO @productoCursor, @numOrdenCursor, @almacenCursor, @cantidadCursor, @cantidadRepartirCursor
END
CLOSE @cursor
DEALLOCATE @cursor

-- Repetimos a la salida del bucle para que actualice el último valor
update @reponer set cantidadReponer = @cantidadReponerCursor - stockDestino, caso = 3 where producto = @productoAnterior and caso is null

if @debug = 0
	delete @reponer where caso = 3 and cantidadReponer <= 0
else
	update @reponer set cantidadReponer = 0 where caso = 3 and cantidadReponer <= 0

-- En este momento no debería quedar nada sin cantidadReposicion, pero si queda algo habría que borrarlo también.
----------------------------------------------------
--select * from @reponer where cantidadReponer is null
/*
-- Por si queremos rellenar desde la tabla inventario
delete @reponer
insert into @reponer (producto,	stockMaximoOrigen, stockOrigen, pendientesOrigen, stockMaximoDestino,	stockDestino, pendientesDestino, cantidadReponer, caso)
select Número, 0, 0, 0, 0, 0, 0, StockReal, 99  from Inventarios where Almacén = 'REI' and estado = 1 --and Familia = 'Essie'
*/

/*
-- Carlos 15/07/24: rellenamos todos los productos que estén por encima del máximo en el origen. Esto lo hacemos en agosto para no tener stock parado en las tiendas.
delete @reponer
;with datosStock as (
	select Número Producto, SUM(cantidad) as Stock
	from ExtractoProducto where Empresa in (@empresa, @espejo, @EMPRESA_VISNU) and Almacén = @AlmacenOrigen
	group by Número
)
insert into @reponer (producto,	stockMaximoOrigen, stockOrigen, pendientesOrigen, stockMaximoDestino, stockDestino, pendientesDestino, cantidadReponer, caso)
select c.Número, 0, 0, 0, 0, 0, 0, d.Stock - c.StockMáximo, 99
from datosStock d inner join ControlesStock c
on c.Empresa = '1' and c.Número = d.Producto and Almacén = @AlmacenOrigen
inner join Productos p
on p.Empresa = '1' and p.Número = d.Producto
where c.StockMáximo < d.Stock
and p.Estado != 4 and not (p.Grupo = 'PEL' and p.SubGrupo = 'APA')
*/

/*
-- Carlos 05/11/25: rellenamos desde un pedido.
delete @reponer
insert into @reponer (producto,	stockMaximoOrigen, stockOrigen, pendientesOrigen, stockMaximoDestino, stockDestino, pendientesDestino, cantidadReponer, caso)
select Producto, 0, 0, 0, 0, 0, 0, Cantidad, 99
from LinPedidoVta
where Número = 903092
*/

-- Carlos 23/04/26: Forzar envío de excedentes de una familia al destino
-- (uso típico: consolidar una familia en ALG cuando sobra en las tiendas)
-- Excedente = stockOrigen - stockMáximoOrigen - pendientesOrigen
-- Si no hay StockMáximo en ControlesStock, se trata como 0 (todo el stock es sobrante).
if @ForzarEnvioFamilia = 1 and @FamiliaForzada is not null begin

    declare @excedentesFamilia table (
        producto char(15),
        stockMaxOri smallint,
        stockOri smallint,
        pdteOri smallint,
        excedente smallint
    )

    insert into @excedentesFamilia (producto, stockMaxOri, stockOri, pdteOri, excedente)
    select
        p.Número,
        isnull(c.StockMáximo, 0),
        isnull(s.Stock, 0),
        isnull(pdte.Cantidad, 0),
        isnull(s.Stock, 0) - isnull(c.StockMáximo, 0) - isnull(pdte.Cantidad, 0)
    from Productos p
        left join ControlesStock c
            on c.Empresa = @empresa
            and c.Almacén = @AlmacenOrigen
            and c.Número = p.Número
        left join (
            select Número, SUM(Cantidad) as Stock
            from ExtractoProducto
            where Empresa in (@empresa, @espejo, @EMPRESA_VISNU)
              and Almacén = @AlmacenOrigen
            group by Número
        ) s on s.Número = p.Número
        left join (
            select Producto, SUM(Cantidad) as Cantidad
            from LinPedidoVta
            where Empresa in (@empresa, @espejo)
              and Almacén = @AlmacenOrigen
              and Estado between -1 and 1
              and Número <> @pedidoCongreso
            group by Producto
        ) pdte on pdte.Producto = p.Número
    where p.Empresa = '1'
      and p.Familia = @FamiliaForzada
      and p.Ficticio = 0
      and p.Estado >= 0
      and isnull(s.Stock, 0) - isnull(c.StockMáximo, 0) - isnull(pdte.Cantidad, 0) > 0

    -- 3.a) Productos que YA están en @reponer: solo actualizar si el excedente
    --      supera la cantidadReponer que ya calculó la lógica normal.
    update r
        set r.cantidadReponer = e.excedente,
            r.caso = 11
    from @reponer r
        inner join @excedentesFamilia e on e.producto = r.producto
    where e.excedente > r.cantidadReponer

    -- 3.b) Productos que NO están en @reponer: insertar con el excedente completo.
    insert into @reponer (producto, stockMaximoOrigen, stockOrigen, pendientesOrigen,
                          stockMaximoDestino, stockDestino, pendientesDestino,
                          cantidadReponer, caso)
    select e.producto, e.stockMaxOri, e.stockOri, e.pdteOri, 0, 0, 0,
           e.excedente, 12
    from @excedentesFamilia e
    where e.producto not in (select producto from @reponer)

end

-- actualizamos los datos del producto
update @reponer set grupo = p.Grupo, texto = isnull(p.Nombre,'<<<PRODUCTO SIN NOMBRE>>>')
from @reponer as r inner join Productos as p
	on p.Empresa = '1' and r.producto = p.Número

-- Hacemos la select con el mismo formato que tenga la que está puesta ahora mismo en el procedimiento.
----------------------------------------------------
if @debug = 0 begin
	delete @reponer where cantidadReponer = 0
	select cantidadReponer, '1'  as Empresa, 'Venta' as PedidoPor, producto as Número, grupo as Grupo, texto as Texto, @almacenDestino as Almacen, stockOrigen as StockOrigen, stockDestino as StockDestino, 0 as StockIntermedio, stockMaximoDestino as CantidadMaximaDestino, pendientesOrigen as CantidadPendienteServirOrigen, pendientesDestino as CantidadPendienteServirDestino, cantidadReponer as CantidadReposicion, 0 as VariasOpciones, 0 as CantidadPedidoEspecial, 1 as Multiplos
		from @reponer order by producto
	--select * from @reponer order by producto
	--select producto, cantidadReponer from @reponer order by producto
end else begin
	select top 50 * from @reponer where caso = 1 order by producto
	select top 50 * from @reponer where caso = 2 order by producto
	select top 50 * from @reponer where caso = 3 order by producto
	select top 50 * from @reponer where caso = 4 order by producto
	select top 50 * from @reponer where caso = 5 order by producto
	select top 50 * from @reponer where caso = 6 order by producto
	select top 50 * from @reponer where caso = 7 order by producto
	select top 50 * from @reponer where caso = 8 order by producto
	select top 50 * from @reponer where caso = 9 order by producto
	select top 50 * from @reponer where caso = 10 order by producto
	select * from @reponer where producto = '36299'
end
GO

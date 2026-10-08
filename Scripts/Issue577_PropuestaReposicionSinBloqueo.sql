/*
    NestoAPI#577 (corte 3a): propuesta de reposición sin el freno de «reposición anterior pendiente», con las cuentas de
    lo que ya está en camino bien hechas y con hora de corte para los pedidos. Procedimiento NUEVO:
    prdRellenarReposicionStock2. El viejo (prdRellenarReposicionStock) se queda como está para Nesto viejo.

    Por qué (08/10/26): Alfredo no pudo calcular la propuesta Algete → Alcobendas porque la 80915 (ALG → ALC, del día
    anterior) aún no estaba recibida en Alcobendas. Cada reposición creada con POST api/Reposiciones ya es independiente
    (cada una con su NºTraspaso), así que el freno sobra. Pero el viejo, además de sumar lo pendiente de recibir del
    destino al stock del DESTINO (bien), lo sumaba también al stock del ORIGEN (líneas «update @reponer set stockOrigen =
    stockOrigen + …» con Almacén = @almacenDestino). Mientras existía el freno eso siempre sumaba 0; sin el freno, el origen
    parecería tener más stock y se propondría mandar dos veces lo mismo.

    CAMBIOS RESPECTO A prdRellenarReposicionStock (el resto del cuerpo es idéntico, para poder compararlos línea a línea):
      1. Sin el freno (raiserror «No se puede rellenar porque hay una reposición anterior pendiente de contabilizar»).
      2. Parámetro @Corte datetime = NULL. Con valor, de los pendientes de pedidos (LinPedidoVta, Estado -1..1, en origen y
         en destino) solo cuentan las líneas con ISNULL(FechaCreacion, [Fecha Modificación]) < @Corte. Con NULL, todas,
         como hoy (lo que pasa el endpoint manual GET api/Reposiciones/Propuesta). El job del corte 3b pasará el instante
         del calendario (día + HoraCierre). FechaCreacion la crea Issue577_LinPedidoVtaFechaCreacion.sql: las líneas
         anteriores a ese script no la tienen y se usa su Fecha Modificación.
      3. stockDestino = ExtractoProducto del destino + lo que ya va hacia él − lo que ya ha salido de él sin pasar al
         extracto. stockOrigen = ExtractoProducto del origen − lo comprometido para salir que aún no está en el extracto.
         Lo que va hacia el ORIGEN y no ha llegado NO cuenta como stock del origen: no se puede mandar lo que no se tiene.
         Se calcula UNA vez por almacén en @enCamino (abajo, con cada estado documentado) y se usa en los dos sitios.
      4. «delete @reponer where stockOrigen = 0» pasa a «<= 0»: al restar lo comprometido el origen puede quedar en negativo.

    ESTADOS DE PreExtrProducto QUE SE CUENTAN (leídos del código que los escribe y comprobados con los datos del 08/10/26:
    80915 y 80917 ALG → ALC y 80919 ALC → ALG, todas pendientes de recibir). Diarios por almacén (Almacenes):
    ALG entrada PendRepo2 / salida General; REI PendRepo / Repo2; ALC RepoAlgAlc / RepoAlcAlg.

      A) EN PREPARACIÓN (sin NºTraspaso). Las líneas viven en el DiarioSalidaRep del ORIGEN con Almacén = DESTINO,
         cantidad positiva y Estado >= 0 (3 al crearlas). Las graban Nesto viejo («Rellenar») y POST api/Reposiciones
         desde una tienda (PreparacionReposicion.SQL_INSERTAR_LINEA). Todavía no hay salida negativa del origen.
           → destino: + Cantidad (va hacia él).   → origen: − Cantidad (comprometida para salir).
         Es la misma definición que GET api/Reposiciones/EnTransito (TransitoReposiciones.SQL_EN_TRANSITO): diario de
         salida de OTRO almacén (Almacenes.DiarioSalidaRep) con Almacén = el que recibe.

      B) TERMINADA EN UNA TIENDA, PENDIENTE DE RECIBIR (Nesto viejo «Contabilizar» o «Terminar» de la API). Al terminar se
         numera, se inserta la salida negativa en el origen y se CONTABILIZA en el momento (pasa a ExtractoProducto), y la
         entrada (positiva, Estado 3, con NºTraspaso) se mueve al DiarioEntradaRep del destino. Ej.: 80919 ALC → ALG, 19
         filas en PendRepo2, Almacén ALG, Delegación ALC.
           → destino: + Cantidad.   → origen: nada (la salida ya está en el extracto).

      C) CREADA DESDE ALGETE (POST api/Reposiciones con control de ubicaciones), POR RECOGER EN ARIADNA. La API la numera
         y deja la salida NEGATIVA en «General» con Almacén = ALG y NºTraspaso, SIN contabilizar, y la entrada positiva en
         el DiarioEntradaRep de la tienda. Al terminar la recogida (Ariadna, OrigenSalidaReposicion) se contabiliza la
         salida.
           → destino (la tienda): + Cantidad de la entrada.   → origen (ALG): − la salida negativa sin contabilizar.

      D) ALGETE YA RECOGIDA (o hecha en Nesto viejo y contabilizada), PENDIENTE DE RECIBIR EN LA TIENDA: solo queda la
         entrada en el DiarioEntradaRep de la tienda. Ej.: 80915 y 80917 ALG → ALC, 30 filas en RepoAlgAlc.
           → destino: + Cantidad.   → origen: nada.

      E) RECIBIDA: la entrada se contabiliza (prdExtrProducto del diario de entrada) y desaparece de PreExtrProducto. Ya
         está en el extracto del destino: no se cuenta aparte.

      Las filas con NºTraspaso se cuentan en cualquier diario, como hacía el viejo (incluido «RepoEscond», donde la API
      aparta otros traspasos DENTRO de la transacción de contabilizar uno: siguen en camino). Con signo:
         - Almacén = X y cantidad positiva: entrada pendiente de recibir en X (estados B, C, D) → + en el destino.
         - Almacén = X y cantidad negativa: salida de X numerada y sin contabilizar (estado C) → − en el origen y también
           − en el destino si es él quien la manda (p. ej. propuesta REI → ALG con una ALG → REI por recoger: Algete ya no
           tiene esas unidades). El viejo ya lo hacía en el destino (sumaba todas las filas con signo).
      No se cuenta lo que va hacia el origen y no ha llegado (lo hacía el viejo, por error, sumándolo al origen).

    VERIFICACIÓN (al final, comentada): ALG → REI, el viejo y el nuevo tienen que dar EXACTAMENTE lo mismo cuando no hay
    nada en camino que el viejo no viera: nada pendiente de recibir en REI (si no, el viejo da el error del freno), nada en
    preparación en General ni en Repo2 y ninguna salida de ALG por recoger. Si ALG tiene una salida por recoger hacia ALC,
    las diferencias solo pueden ser esos productos, con StockOrigen menor en el nuevo. (Lo que ALG tenga pendiente de
    recibir de las tiendas no cambia nada: el viejo tampoco lo sumaba al origen, porque miraba Almacén = destino.)
    El «<= 0» del punto 4 no cambia nada sin cosas en camino: con stock negativo en el origen el viejo tampoco proponía
    nada (se caían en el caso 5, 9 o 3).

    ORDEN: DESPUÉS de Issue577_LinPedidoVtaFechaCreacion.sql (lee la columna FechaCreacion: sin ella no se puede crear).
    ANTES del deploy de la API: la API pasa a llamar a prdRellenarReposicionStock2 y, sin él, la propuesta y crear una
    reposición sin líneas fallarían. Idempotente (CREATE OR ALTER).

    Ejecutar en SSMS contra NV como sa (el login nuevavision no tiene ALTER ni puede crear procedimientos).
*/

SET NOCOUNT ON;
USE NV;
GO

CREATE OR ALTER PROCEDURE [dbo].[prdRellenarReposicionStock2]  @Empresa as char(3),@AlmacenOrigen as char(3),@AlmacenDestino as char(3), @Corte as datetime = NULL AS
-- NestoAPI#577 (corte 3a, 08/10/26): copia de prdRellenarReposicionStock sin el freno de «reposición anterior pendiente»,
-- con lo que está en camino contado en los dos almacenes (@enCamino) y con @Corte para los pendientes de pedidos.
-- Ver Scripts/Issue577_PropuestaReposicionSinBloqueo.sql en NestoAPI.
-- declaraciones
/*
declare @empresa as char(3) = '1'
declare @almacenOrigen as char(3) = 'REI'
declare @almacenDestino as char(3) = 'ALG'
declare @Corte as datetime = NULL
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

-- NestoAPI#577 (corte 3a): SIN el freno «No se puede rellenar porque hay una reposición anterior pendiente de
-- contabilizar». Cada reposición es independiente (su NºTraspaso) y lo que ya está en camino se cuenta abajo (@enCamino).

-- NestoAPI#577 (corte 3a): las líneas de pedido que cuentan como pendientes. Con @Corte, solo las creadas antes del corte
-- (ISNULL(FechaCreacion, [Fecha Modificación]): las anteriores a la columna usan su fecha de modificación). Sin @Corte, todas.
declare @lineasPendientes table (
	[Nº Orden] int primary key,
	producto char(15),
	almacen char(3),
	cantidad smallint,
	fechaModificacion datetime
)
insert into @lineasPendientes ([Nº Orden], producto, almacen, cantidad, fechaModificacion)
select [Nº Orden], Producto, Almacén, Cantidad, [Fecha Modificación]
from LinPedidoVta
where Empresa in (@empresa, @espejo) and Almacén in (SELECT almacen FROM @almacenesRepo) and Estado between -1 and 1
and Número <> @pedidoCongreso
and (@Corte is null or isnull(FechaCreacion, [Fecha Modificación]) < @Corte)

-- NestoAPI#577 (corte 3a): lo que está en camino, por almacén y producto (estados A-E en la cabecera del script).
--   entrante: va hacia el almacén y aún no ha entrado en su extracto (en preparación hacia él o pendiente de recibir).
--   saliente: ha salido (o está preparado para salir) del almacén y aún no ha salido de su extracto.
declare @enCamino table (
	almacen char(3),
	producto char(15),
	entrante int not null default(0),
	saliente int not null default(0),
	primary key (almacen, producto)
)
;with movimientos as (
	-- Con NºTraspaso, en cualquier diario (como el viejo). Positiva: entrada pendiente de recibir (estados B, C, D).
	-- Negativa: salida numerada sin contabilizar (estado C, Algete por recoger en Ariadna).
	select p.Almacén as almacen, p.Número as producto,
		case when p.Cantidad > 0 then p.Cantidad else 0 end as entrante,
		case when p.Cantidad < 0 then -p.Cantidad else 0 end as saliente
	from PreExtrProducto p
	where isnull(p.NºTraspaso, 0) <> 0 and p.Almacén in (@almacenOrigen, @almacenDestino)
	union all
	-- En preparación (estado A), entrante en el que recibe: sin NºTraspaso, en el diario de salida de reposiciones de OTRO
	-- almacén, con Almacén = el que recibe (la misma definición que TransitoReposiciones.SQL_EN_TRANSITO de la API).
	select p.Almacén, p.Número, p.Cantidad, 0
	from PreExtrProducto p
	where isnull(p.NºTraspaso, 0) = 0 and p.Estado >= 0 and p.Cantidad > 0 and p.Almacén in (@almacenOrigen, @almacenDestino)
	and exists (select 1 from Almacenes o where o.Empresa = p.Empresa and o.Número <> p.Almacén and o.DiarioSalidaRep = p.Diario)
	union all
	-- En preparación (estado A), saliente en el que manda: las mismas líneas vistas desde el almacén dueño del diario de
	-- salida (todavía no tienen la línea negativa: se crea al terminar).
	select o.Número, p.Número, 0, p.Cantidad
	from PreExtrProducto p
	inner join Almacenes o on o.Empresa = p.Empresa and o.DiarioSalidaRep = p.Diario and o.Número <> p.Almacén
	where isnull(p.NºTraspaso, 0) = 0 and p.Estado >= 0 and p.Cantidad > 0 and o.Número in (@almacenOrigen, @almacenDestino)
	and p.Almacén in (SELECT almacen FROM @almacenesRepo)
)
insert into @enCamino (almacen, producto, entrante, saliente)
select almacen, producto, sum(entrante), sum(saliente)
from movimientos
group by almacen, producto

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

-- Insertamos los que tienen stock máximo
insert into @reponer (producto, stockMaximoDestino)
select Número, StockMáximo from ControlesStock where Empresa = @empresa and Almacén = @almacenDestino and StockMáximo>0

-- insertamos los que tienen pendientes y no tienen stock máximo
insert into @reponer (producto)
select producto from @lineasPendientes
where almacen = @almacenDestino and producto not in (select producto from @reponer)
group by producto

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

-- Carlos 30/08/23: modificamos para que se tenga en cuenta desde Reina lo pendiente en Alcobendas y viceversa
-- Rellenamos PendientesDestino
update @reponer set pendientesDestino = s.Cantidad
from @reponer as r inner join
(
	select producto, isnull(SUM(cantidad),0) as Cantidad
	from @lineasPendientes
	where almacen = @almacenDestino and producto in (select producto from @reponer)
	group by producto
) as s
on r.producto = s.producto

-- NestoAPI#577 (corte 3a): al stockDestino, lo que va hacia él (en preparación o pendiente de recibir) menos lo que ha
-- salido de él sin contabilizar. Antes: todas las filas con NºTraspaso y Almacén = destino (lo mismo, salvo que no
-- contaba lo que está en preparación hacia él).
update @reponer set stockDestino = stockDestino + c.entrante - c.saliente
from @reponer as r inner join @enCamino as c
on c.almacen = @almacenDestino and r.producto = c.producto

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

-- NestoAPI#577 (corte 3a): al stockOrigen, menos lo comprometido para salir que aún no está en el extracto (en
-- preparación o salida numerada sin contabilizar). Lo que va HACIA el origen no cuenta: aún no lo tiene.
update @reponer set stockOrigen = stockOrigen - c.saliente
from @reponer as r inner join @enCamino as c
on c.almacen = @almacenOrigen and r.producto = c.producto

-- Los que el stock origen sea cero las borramos
----------------------------------------------------
if @debug = 0
	delete @reponer where stockOrigen <= 0
else
	update @reponer set caso = 8 where stockOrigen <= 0

-- Calculamos en el origen pendientes, stock máximo y sumamos las pendientes de recibir de las repos anterior al stock
-- En las que stockOrigen >= (stockMaxOrigen + PdteOrigen + stockMaxDestino + PdteDestino - stockDestino) calculamos CantidadReposicion y nos olvidamos de ellos
	--> de aquí en adelante ya no entra ninguna de las que tienen cantidadReposicion
----------------------------------------------------
update @reponer set pendientesOrigen = s.Cantidad
from @reponer as r inner join
(
	select producto, isnull(SUM(cantidad),0) as Cantidad
	from @lineasPendientes
	where almacen = @almacenOrigen and producto in (select producto from @reponer)
	group by producto
) as s
on r.producto = s.producto

-- NestoAPI#577 (corte 3a): aquí el viejo sumaba al stockOrigen lo pendiente de recibir del DESTINO
-- (PreExtrProducto con Almacén = @almacenDestino y NºTraspaso). Con el freno siempre sumaba 0; sin él, sería mandar dos
-- veces lo mismo. Quitado: lo que está en camino ya se ha contado arriba en cada almacén.

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
update @reponer
set cantidadReponer = pendientesDestino - stockDestino, caso = 2
where stockOrigen + stockDestino >= pendientesOrigen + pendientesDestino and caso is null

-- Los de caso 2 que no sale a reponer nada, dividimos el stock proporcionalmente
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
select l.producto, l.[Nº Orden], l.almacen, l.cantidad, l.fechaModificacion
from @lineasPendientes as l inner join @reponer as r
on l.producto = r.producto
where r.caso is null

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
        isnull(s.Stock, 0) - isnull(ec.saliente, 0),
        isnull(pdte.Cantidad, 0),
        isnull(s.Stock, 0) - isnull(ec.saliente, 0) - isnull(c.StockMáximo, 0) - isnull(pdte.Cantidad, 0)
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
            select producto, SUM(cantidad) as Cantidad
            from @lineasPendientes
            where almacen = @AlmacenOrigen
            group by producto
        ) pdte on pdte.producto = p.Número
        left join @enCamino ec on ec.almacen = @AlmacenOrigen and ec.producto = p.Número -- NestoAPI#577: lo comprometido para salir
    where p.Empresa = '1'
      and p.Familia = @FamiliaForzada
      and p.Ficticio = 0
      and p.Estado >= 0
      and isnull(s.Stock, 0) - isnull(ec.saliente, 0) - isnull(c.StockMáximo, 0) - isnull(pdte.Cantidad, 0) > 0

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
	select top 50 * from @enCamino order by almacen, producto
end
GO

-- Solo lee (trabaja con variables de tabla): EXECUTE a la cuenta con la que corre NestoAPI (NestoConnection).
GRANT EXECUTE ON dbo.prdRellenarReposicionStock2 TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN (comentada; lanzar a mano). El viejo y el nuevo, ALG → REI, sin corte. Cuando REI no tenga nada pendiente
-- de recibir (si no, el viejo da el error del freno) y ALG no tenga reposiciones en preparación ni salidas por recoger,
-- tienen que dar EXACTAMENTE lo mismo: las dos consultas finales, 0 filas. Si ALG tiene una salida por recoger hacia ALC,
-- solo pueden salir esos productos, con StockOrigen menor en el nuevo (y quizá menos CantidadReposicion).
------------------------------------------------------------------------------------------------
/*
DECLARE @viejo TABLE (cantidadReponer smallint, Empresa varchar(3), PedidoPor varchar(10), Número char(15), Grupo char(3),
    Texto char(50), Almacen char(3), StockOrigen smallint, StockDestino smallint, StockIntermedio int, CantidadMaximaDestino smallint,
    CantidadPendienteServirOrigen smallint, CantidadPendienteServirDestino smallint, CantidadReposicion smallint, VariasOpciones int,
    CantidadPedidoEspecial int, Multiplos int);
DECLARE @nuevo TABLE (cantidadReponer smallint, Empresa varchar(3), PedidoPor varchar(10), Número char(15), Grupo char(3),
    Texto char(50), Almacen char(3), StockOrigen smallint, StockDestino smallint, StockIntermedio int, CantidadMaximaDestino smallint,
    CantidadPendienteServirOrigen smallint, CantidadPendienteServirDestino smallint, CantidadReposicion smallint, VariasOpciones int,
    CantidadPedidoEspecial int, Multiplos int);

-- ¿Algo en camino que haga que no coincidan? Tiene que salir vacío para comparar sin dudas (lo pendiente de recibir en ALG
-- no molesta: diario PendRepo2, no sale aquí).
SELECT Diario, Almacén, Delegación, NºTraspaso, COUNT(*) AS filas, SUM(Cantidad) AS unidades
FROM PreExtrProducto WITH (NOLOCK)
WHERE (ISNULL(NºTraspaso, 0) <> 0 AND (Almacén = 'REI' OR (Almacén = 'ALG' AND Cantidad < 0)))
   OR (ISNULL(NºTraspaso, 0) = 0 AND Diario IN ('General', 'Repo2'))
GROUP BY Diario, Almacén, Delegación, NºTraspaso;

INSERT INTO @viejo EXEC dbo.prdRellenarReposicionStock '1', 'ALG', 'REI';
INSERT INTO @nuevo EXEC dbo.prdRellenarReposicionStock2 '1', 'ALG', 'REI', NULL;

SELECT 'solo en el viejo' AS donde, * FROM (SELECT * FROM @viejo EXCEPT SELECT * FROM @nuevo) AS v;
SELECT 'solo en el nuevo' AS donde, * FROM (SELECT * FROM @nuevo EXCEPT SELECT * FROM @viejo) AS n;
SELECT (SELECT COUNT(*) FROM @viejo) AS lineasViejo, (SELECT COUNT(*) FROM @nuevo) AS lineasNuevo;

-- Y el caso que motivó el cambio: ALG → ALC con la 80915/80917 sin recibir. El viejo da el error; el nuevo, la propuesta
-- con lo que ya va en camino sumado a StockDestino.
EXEC dbo.prdRellenarReposicionStock2 '1', 'ALG', 'ALC', NULL;

-- Con corte: solo cuentan los pendientes creados antes de las 10:00 de hoy (las líneas sin FechaCreacion, por su Fecha
-- Modificación). Tiene que dar lo mismo o menos que sin corte.
DECLARE @corte datetime = DATEADD(HOUR, 10, CAST(CAST(GETDATE() AS date) AS datetime));
EXEC dbo.prdRellenarReposicionStock2 '1', 'ALG', 'REI', @corte;
*/

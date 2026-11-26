# Inventario y stock

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Inventarios, rellenado, stock de la web, altas y bajas, pérdida desconocida, almacenes ficticios.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Inventario por huecos en almacenes con ubicaciones (incidencia 1227, 12/11/26)
- `POST api/Inventarios` en un almacén con `Almacenes.ControlUbicaciones` de un producto con `Ubicar` exige Pasillo, Fila y Columna (si no, 400
  «En {almacén} el inventario va por huecos…») y pone de teórico (`StockCalculado`) lo LIBRE de ese producto en ese hueco (Ubicaciones Estado 0,
  0 si no estaba), como el rellenado de Ariadna (`ILectorHuecoInventario`). `PUT` que quitaría el hueco a una fila que lo tenía: 400. Tiendas,
  productos que no se ubican, «Dejar a cero», rellenado de Ariadna y «No está» no cambian (no pasan por aquí).

### Inventario pendiente de otro día y el picking (incidencias 1227 y 1057, 12/11/26)
- `DELETE api/Inventarios/Filas/{n}`: una fila PENDIENTE de un día ANTERIOR a hoy se borra siempre (`ReglaBorrarFilaInventario.EsDeDiaAnterior`,
  sin mirar si sobra; mismos permisos, applocks y rastro); las de hoy, solo las que sobran; las de días posteriores y las contabilizadas, nunca.
  `Marcar` pone `SePuedeBorrar` en las de `OtraFecha` anteriores a hoy (no en las de hoy al comprobar otro día). El aviso `FilasBorradas` saca las
  borradas ese día aunque la fila fuera de otro, con «(fila del dd/MM/yyyy)». Nesto y Ariadna pintan el botón solo por `SePuedeBorrar`.
- **Dos filas de días distintos se bloqueaban entre sí (novedad 1364, ALC, 19/11/26)**: la fila de ayer salía sin «Borrar» porque la usuaria
  de tienda no tenía «InventarioBorrarFilas». Ahora su PROPIA fila pendiente de un día anterior la borra quien puede contar en el almacén
  (`ReglaBorrarFilaInventario.PuedeBorrarEsta`/`EsDelUsuario`; Marcar y DELETE). Y «Contabilizar también la del dd/MM»: GET Comprobaciones y POST
  Contabilizar con `incluirDiasAnteriores` cuentan las pendientes de días ANTERIORES a la fecha como del día (se comprueban con él, entran en el
  recuento, `Dias` por usuario; `IncluyeDiasAnteriores`, `DiasAnterioresIncluidos`). El SP sigue siendo uno por usuario (ya contabilizaba todas
  las suyas: por eso OtraFecha bloquea). OtraFecha trae `SePuedeIncluir` (todas anteriores); con una de un día posterior, elegir esa fecha.
- `PUT api/Inventarios/{id}` cambiando Fecha (o Almacén, Empresa, Número) daba 500: son parte de la clave en el EDMX y EF las pone en el WHERE
  del UPDATE con el valor nuevo (0 filas → DbUpdateConcurrencyException). Ahora 400 (`ReglaClaveFilaInventario`); lo que no viene, misma fecha
  con otra hora o relleno se toman de la guardada; una concurrencia de verdad, 409.
- Picking cortado por los disparadores del inventario («Se esta realizando el inventario del producto[1]»): `GestorPicking.EnExclusiva` lo
  traduce (`ErrorInventarioPendientePicking`, pura) a un mensaje con las filas pendientes (fuera de REI) de los productos del picking (o todas si
  no casa ninguna; 10 y «… y N más»). Es un `Exception` (INTERNAL_ERROR, sin «[CÓDIGO]» en Nesto) con el original dentro; si la consulta falla, el original.
- **Enviar una reposición (incidencia 1455, Paloma, 25/11/26)**: núcleo común `Inventarios/ErrorInventarioPendiente` (reconocer, acotar, listar, traducir;
  el picking delega en él sin cambiar nada). Terminar y la puntual (`ServicioPreparacionReposicion.ExplicandoInventarioPendiente`) cambian el texto por LA REGLA
  `ErrorInventarioPendienteReposicion` («No se puede enviar la reposición: en Alcobendas hay inventario sin contabilizar (24870 TANGA…, del 24/11, de Paloma).
  Contabilícelo o borre esa fila en Inventario y vuelva a enviarla; lo leído sigue guardado.»): las pendientes del ORIGEN (cualquier producto, las suyas
  primero), 10 como mucho; mismo código de estado (otra `NestoBusinessException` con el del original). Sin filas o sin lectura, el original. Sin traducir
  todavía: recibir una reposición, terminar una salida REPO/PICK de Ariadna, traspasos de almacenes ficticios, «No está», diferencias de reposición.

### Deshacer el rellenado del inventario de una tienda (12/11/26)
- `DELETE api/Inventarios/Rellenar?almacen=REI&fecha=2026-11-12[&empresa=1]` (`ServicioDeshacerRellenarInventario`, sin UI: a mano con el token): borra
  TODAS las filas pendientes (Estado 1, NºTraspaso NULL) de ese almacén y día, de cualquier usuario y aunque estén contadas. Solo Dirección e
  Informática (403); fecha obligatoria y almacén sin ubicaciones (400). Applock `InventarioRellenar:{empresa}:{almacen}` y transacción; nunca lo
  contabilizado ni ExtractoProducto. 200 `{ Almacen, Fecha, FilasBorradas, FilasContadas, UnidadesContadas }` (ceros si no hay nada, y sin rastro);
  rastro = UNA fila en Modificaciones (Tabla «InventarioRellenadoDeshecho», resumen en JSON con el desglose por usuario).
- **Rellenar por error (Paloma, 25/11/26: 3.032 filas en Alcobendas)**: (1) `GET api/Inventarios/Rellenar/Previa?almacen=&fecha=&empresa=` (permisos de
  rellenar; solo lectura) → {Almacen, NombreAlmacen, Fecha, FilasQueSeCrearian (MISMA consulta `SqlRellenadoInventario.SQL_STOCKS` y `FilasARellenar`;
  0 si ya hay pendientes), FilasPendientesYa, Mensaje «Va a crear 3.032 filas de inventario en Alcobendas con fecha 25/11/2026.», PuedeDeshacer,
  MotivoNoPuedeDeshacer}. (2) `POST api/Inventarios/Rellenar` exige `Confirmar` (cuerpo, o `?confirmar=`) con el código o el nombre del almacén
  (`ReglaConfirmacionRellenado`: sin mayúsculas ni tildes; vale una palabra del nombre de 4+ letras) SOLO si la petición NO trae `X-Ariadna-Version`
  (Ariadna llama al mismo endpoint y no cambia) y solo cuando se iban a crear filas (YaRellenado/YaContabilizado salen igual); si no, 400
  {Codigo «CONFIRMACION_NECESARIA», Message «Para rellenar el inventario de Alcobendas escriba el nombre del almacén.»} y nada creado. (3) El DELETE
  también lo puede quien puede rellenar si TODAS las pendientes son suyas y ninguna contada (`ReglaDeshacerRellenadoPropio`, comprobado con UPDLOCK/HOLDLOCK
  en la transacción): si no, 403 «hay filas de otro usuario (…)» o 409 «ya hay N filas contadas: pídaselo a Dirección.». Dirección e Informática, como antes.
- **Borrar desde la lista «Completo» (25/11/26)**: `GET api/Inventarios?empresa&almacen&fecha` trae en cada fila `SePuedeBorrar` (`Inventario.Partial.cs`,
  NotMapped, setter interno: un POST/PUT no lo cambia) con LA REGLA del DELETE de filas (`ReglaBorrarFilaInventario.SePuedeBorrarFila`: puede contar,
  `PuedeBorrarEsta`, pendiente, no posterior a hoy y, si es de hoy, que sobre; las comprobaciones de hoy se leen una vez y solo si hace falta). Con
  `incluirPendientesAnteriores=true` (por defecto false) añade al final las pendientes de días ANTERIORES del almacén con `PendienteDeDiaAnterior` y su Fecha.

### Stock de la web al momento (NestoAPI#818, 05/11/26)
- La fuente de verdad es `ExtractoProducto`: el disparador `trgExtractoProducto_SyncTienda` (AFTER INSERT/UPDATE/DELETE,
  `Scripts/Issue818_ExtractoProducto_EncolarSyncTienda.sql`) encola en `Nesto_sync` cada producto (y sus kits) cuya sentencia
  no sume cero por producto y almacén (empresas 1 y 3; ALG, REI, ALC; ficha de la empresa 1 con Estado >= 0; sin repetir pendientes).
  El +1/-1 de la carpeta o de un cambio de ubicación suma cero y no encola (Carlos 05/11/26). Nunca tumba el apunte (TRY/CATCH sin ROLLBACK).
- No hace falta encolar a mano al recibir, inventariar o regularizar, ni pasar `sincronizar-stocks-nocturno` a horario: queda
  de red (LinPedidoVta por fuera de la API, DescuentosProducto, encolados perdidos). Tras encolar, `@@IDENTITY` es el Id de Nesto_sync.

### Altas y bajas de stock de las tiendas: correo mensual (NestoAPI#886, 23/11/26)
- Al enviar una reposición, lo que no está se da de baja («No está») y, si aparece, de alta (sugerencia 1419). Para controlarlo, cada tienda
  (REI, ALC) recibe una vez al mes lo dado de alta y de baja en su stock: ExtractoProducto de las empresas 1 y 3, su almacén, diarios
  `_RegInvent` (motivo «Inventario») y `_ErrRepo` («No está» al enviar la reposición N / Diferencia de la reposición N, por el Texto; si no casa,
  el texto). UNA consulta de solo lectura (NOLOCK) por almacén y periodo; LA REGLA (pura) `GestorAltasBajasTienda.Construir`: líneas por fecha y
  hora, usuario sin dominio, valor SIEMPRE a PVP (`Productos.PVP` empresa 1; nulo = 0; NUNCA coste: lo ven las tiendas), totales de altas, bajas
  y neto, y «Aparece y desaparece» (productos con alguna alta Y alguna baja, con su neto).
- `GET api/Informes/AltasBajasTienda?almacen=&desde=&hasta=` y `/Excel` (hojas «Movimientos», «Aparece y desaparece», «Resumen»): permisos y
  tienda de FacturacionTienda (`ValidarTiendaFacturacion`); sin fechas, el mes natural anterior; ≤ 366 días. `POST …/AltasBajasTienda/Correo?almacen=&desde=&hasta=`
  (Dirección, Informática, Compras; 403 de usted) y el job `tiendas-altas-bajas-mensual` (día 1 a las 8:15 de Madrid, mes anterior, REI y ALC;
  un fallo por tienda a ELMAH) van por el MISMO `EnviadorAltasBajasTienda`: a `Constantes.Correos.TIENDA_REINA`/`TIENDA_ALCOBENDAS` con CC a
  `Correos.COMPRAS`, HTML (300 líneas como mucho; el resto en el Excel adjunto). Sin movimientos, no se manda nada. Sin tabla ni script.

### Pérdida desconocida por almacén, acumulada desde el 1 de enero (NestoAPI#893 corte 1, 25/11/26)
- `GET api/Informes/PerdidaDesconocida?anio=&hasta=` y `/Excel` (hojas «Resumen», «Productos», «Diarios sin clasificar», «Notas»): SOLO Dirección e
  Informática (va a COSTE: `Productos.PrecioMedio` de hoy, como IndicadoresStock). Sin nada, este año hasta hoy; año pasado, hasta el 31/12; `hasta`
  ≤ hoy. UNA consulta agregada de solo lectura (NOLOCK, `GestorPerdidaDesconocida.SQL_PERDIDA_DESCONOCIDA`: ExtractoProducto 1+3, ALG/REI/ALC, por
  almacén, mes, producto y diario). Cuentan `_RegInvent` y `_ErrRepo` (los de #886). LA REGLA (pura) `GestorPerdidaDesconocida.Construir`: por
  producto, almacén y periodo se neta; negativo = falta, positivo = sobra; `Cuadrar` compensa la falta de un almacén con la sobra de otro del MISMO
  producto y periodo (mes, o el año en el acumulado; orden ALG, REI, ALC); lo que no cuadra = pérdida de verdad. «Aparece y desaparece» por producto.
- Los demás diarios A MANO (sin cliente, proveedor, pedido, albarán ni traspaso; fuera `_MontarKit` y `_Ficticio`) salen en `DiariosSinClasificar` y
  NO cuentan hasta que Dirección diga cuáles son pérdida y cuáles tienen causa. «En tránsito» (#906): bloque propio sin imputar a ningún almacén, hoy a
  cero (`DIARIOS_EN_TRANSITO` vacío: se rellena con el diario que escriba la resolución «pérdida en tránsito»). Sin correo ni job todavía; sin cuadre de
  referencias parecidas (44411/44412).

### Almacenes ficticios de vendedores (NestoAPI#894, 24/11/26)
- Sustituye a «Altas y bajas de almacén ficticio» de Nesto viejo: Almacenes.AlmacénFicticio = 1 (IF, AB, IM… de vendedores; PRV, AMZ, EXT, SDC,
  PAC…). Saldo = SUM(ExtractoProducto.Cantidad) empresas 1+3 por producto. Dueño = ParametrosUsuario empresa 1 `AlmacenFicticio` PROPIO (fila
  «(defecto)» vacía, `Scripts/Issue894_AlmacenesFicticios.sql`; nunca se crea al leer); «es suyo» = ese usuario o su `Vendedor` = el del que pregunta
  (claim «Vendedor» de NestoApp o su parámetro).
- LA REGLA (pura) `ReglasAlmacenesFicticios.Decidir` (`Infraestructure/AlmacenesFicticios/`): ver = Dirección, Informática, Almacén y Tiendas todos;
  cada vendedor el suyo; el jefe de equipo los de su equipo (`VendedoresEquipoString`); la tienda online, AMZ; un cliente de la tienda nada (403).
  Meter y sacar, SIEMPRE contra el almacén propio: de vendedor (tipo `Vendedor` = tiene dueño), Almacén (ALG) y Tiendas (su AlmacénPedidoVta REI/ALC,
  `GestorFacturacionTienda.NormalizarTienda`; la tienda va antes que Almacén); PRV y EXT solo Almacén; AMZ Almacén y `TiendaOnline` (ALG); los demás
  solo Almacén. Dirección e Informática consultan (no traspasan si no son de Almacén o Tiendas). Sin dueño todavía = `Otro`.
- `GET api/AlmacenesFicticios` → [{Codigo, Descripcion, Tipo, EsDeVendedor, Vendedores, EsSuyo, PuedeTraspasar, AlmacenContra, MotivoNoPuedeTraspasar,
  PermiteNegativo}]; `GET …/{almacen}/Existencias` → {Almacen, Descripcion, Productos[{Producto, Nombre, Familia, NombreFamilia, Unidades}],
  NumeroProductos, TotalUnidades} (saldo ≠ 0; sin costes ni precios; 403/404). `POST …/Traspasos?ensayo=` {AlmacenFicticio, Sentido Entregar|Recoger,
  Lineas[{Producto, Cantidad > 0, Pasillo?, Fila?, Columna?}], Comentario ≤500, Clave?} → {Ensayo, NumeroTraspaso, AlmacenFicticio, AlmacenPropio,
  Sentido, Lineas[{…, Hueco, HuecoPropuesto, PendienteDeUbicar}], Saldos, UnidadesPendientesDeUbicar, Mensaje}. 400 producto que no existe o ficticio
  (anulado solo al entregar: recoger lo anulado vale), stock del origen insuficiente (salvo `Almacenes.PermitirNegativo` del origen); 403; 404; 409 si
  el stock o el hueco cambian al guardar. Misma Clave → la misma respuesta (Modificaciones, Anterior = la clave).
- Algete (ControlUbicaciones + Productos.Ubicar): al ENTREGAR, el hueco de cada línea (el pedido o el que propone la API: FIFO con libre para la línea
  entera; lo libre = estado 0 sin pedido, albarán ni reserva, empresas 1+3) y se quita por la puerta (`SalidaDeHuecoAFicticio`, todo o nada); al
  RECOGER, «pendiente de ubicar» (`EntradaDeFicticio`, estado 2) para la PDA. Tiendas sin huecos.
- Escritura en UNA transacción: applock `TraspasoAlmacenFicticio:1`, stock releído con UPDLOCK, NºTraspaso de `NumeradorTraspasos`, por producto −N
  en el origen y +N en el destino (Texto = nombre del producto, delegación ALG y forma de venta VAR) en el diario de sistema `_Ficticio` por el NÚCLEO
  `MovimientoTraspasoStockSql` (PreExtrProducto + prdExtrProducto por `ServicioExtractoProducto`, apartando otros traspasos del diario; lo usa también
  el ajuste de #634), los huecos y UNA fila en Modificaciones (Tabla `TraspasoAlmacenFicticio`, Nuevo = el resultado + usuario y comentario). Después,
  campana de Nesto (`TraspasoAlmacenFicticio`) a los dueños que no son quien lo hace (nunca rompe). Gemelas NestoApp#292 (consulta) y Nesto#651
  (consulta y pantalla de meter/sacar).

### Productos incompletos: sacar una pieza sin que sea un inventario (NestoAPI#902, 24/11/26)
- Caso: el cable 43415 se daba de alta por inventario sacándolo de un Depiblend 40252 nuevo, que seguía contando como disponible. NO son kits (ni la
  tabla Kits ni lista de piezas): cualquier producto puede ser pieza; la API solo RECUERDA lo sacado (`GET …/PiezasSacadas?producto=`, para proponer).
- `dbo.ProductosIncompletos` (fuera del EDMX, `Scripts/Issue902_ProductosIncompletos.sql`; sin ella, 503): donante, NumeroSerie, pieza, cantidad, pedido,
  huecos, Estado Incompleto/Completado/Baja (CHECK), traspasos, Clave. Almacén de incompletos = ParametrosUsuario «(defecto)» `AlmacenIncompletos`
  (un ficticio activo que NO sea ALG/REI/ALC y sin ControlUbicaciones, `MotivoAlmacenIncompletosNoValido`; si no, 503). No cuenta como disponible
  porque stock (`ServicioGestorStocks`), motor de fechas (`StockSedes`) y tienda (#818) solo suman `Constantes.Sedes.ListaSedes`.
- `api/Almacen/ProductosIncompletos` (`ProductosIncompletosController`, reglas puras `ReglasProductosIncompletos`): `POST SacarPieza?ensayo=` {Almacen,
  ProductoDonante, Pasillo/Fila/Columna? (Algete; si no, propone `ReglasAlmacenesFicticios.AsignarHuecos`), NumeroSerie (OBLIGATORIO con
  `Productos.NecesitaNumSerie`), Pieza, Cantidad, Pedido?, Comentario?, HuecoDestino?, Clave?}; `POST {id}/Completar?ensayo=` {hueco de la pieza?};
  `POST {id}/Baja?ensayo=` {Motivo}; `POST {id}/Etiqueta`; `GET ?soloVivos=true&almacen=&producto=` (Fichas, UnidadesIncompletas, ValorPvp a PVP);
  `GET {id}`. Operar: Almacén, Dirección, Informática (cualquier sede) y Tiendas (su AlmacénPedidoVta); consultar, además Compras.
- Apuntes, en UNA transacción (applock `ProductoIncompleto:1`, releído con UPDLOCK, NºTraspaso de `NumeradorTraspasos`) por el núcleo
  `MovimientoTraspasoStockSql`: SACAR (diario `_SacaPieza`) +N pieza en el almacén y, si la unidad no estaba ya apartada, −1 donante en el almacén
  y +1 en el de incompletos; COMPLETAR (`_SacaPieza`) −N pieza y, si no le queda otra ficha viva, −1 incompletos +1 almacén; BAJA (`_BajaIncom`)
  −1 donante en incompletos (cierra todas las fichas de la unidad). Misma unidad = mismo donante y nº de serie (sin serie, cada ficha es su unidad).
  Algete por la puerta: `SalidaPorProductoIncompleto` (del hueco; sin hueco, pendiente de ubicar y luego el único hueco; todo o nada, 409) y
  `EntradaPorProductoIncompleto` (pendiente de ubicar). Rastro en Modificaciones (Tabla `ProductoIncompleto`). Después, sin romper: etiqueta
  «INCOMPLETO» 3×2 (`GeneradorEtiquetaIncompleto`, Code128 `INC{Id}`) por la `ImpresoraCodBarras` del usuario y campana a Compras («Se debe…»).
- **El almacén «INC» (Carlos 26/11/26)**: «Incompletos», FICTICIO. `MotivoAlmacenIncompletosNoValido` exige ahora además `AlmacénFicticio` = 1. El paso 3 del
  script lo da de alta COPIANDO la fila de otro ficticio (PRV o el primero ficticio activo sin ubicaciones) por `sys.columns` y fijando Número, Descripción,
  AlmacénFicticio 1, ControlUbicaciones 0, PermitirNegativo 0, Estado 0 (y Usuario/Fecha Modificación si existen); las demás columnas, como la plantilla
  (revisar con el SELECT del script). El paso 4 pone `AlmacenIncompletos` = INC si estaba vacío.
  No cuenta en: stock de la web (#818, ALG/REI/ALC), motor de fechas y disponible (`ListaSedes`), reposición del exceso (origen REI/ALC), pérdida desconocida
  (ALG/REI/ALC; además los apuntes llevan NºTraspaso). SÍ cuenta (todos los almacenes menos EXT): indicadores de stock y propuestas de stock (Tendencias,
  `DemandaStock.SQL_FILTRO_STOCK`), precio medio (todas).
- **Que no se cuele por otros caminos (Fable con el criterio de Carlos «nada se pierde, todo por su cauce», 26/11/26)**: `GuardaAlmacenIncompletos`
  (`ProductosIncompletos/GuardaAlmacenIncompletos.cs`; código del parámetro `AlmacenIncompletos` con caché de 5 min, nunca «INC» a fuego; sin fila o si no se
  puede leer, no frena nada). (a) `POST api/Inventarios/Rellenar`, `POST`/`PUT api/Inventarios`, `POST …/DejarACero` y `POST …/Contabilizar` en ese almacén → 400
  {Message «El almacén de incompletos no se inventaría: sus unidades se controlan desde Productos incompletos.»}. (b) Almacenes ficticios (#894): no sale en
  `GET api/AlmacenesFicticios` y `POST …/Traspasos` → 403 «El almacén de incompletos no admite traspasos de almacenes ficticios: …» (las Existencias, solo
  lectura, siguen). (c) No sale en `GET api/PedidosVenta/Almacenes` (`PedidosVentaController.QuitarAlmacenIncompletos`). Propuestas e indicadores de stock, sin cambios.
- Pendiente: aviso al vender una serie incompleta, costes, apartado en el informe de pérdida desconocida (#893), regularizar los Depiblend 101268
  y 101270. Gemelas Nesto#653 (pantalla) y Ariadna#109 (PDA).

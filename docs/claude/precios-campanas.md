# Precios, ofertas y campañas

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Vigencias de la tienda, N+M, campañas en bloque, descuentos autorizados, «Ofertas para ti» y su push.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Pedidos de la tienda: ofertas vigentes el día del pedido (feria online 12-16/11/26)
- `GestorPrecios.EsPedidoValido` valida un pedido de la tienda online (`VigenciaPedidoTienda.EsPedidoTiendaOnline`: «TOTAL PEDIDO:»
  en comentarios + todas las líneas WEB; la misma regla usa `ValidadorRegalosTiendaOnline`) con la vigencia de DescuentosProducto,
  OfertasPermitidas, combinadas, escalonadas y regalos por importe del día `pedido.fecha` (en el PUT, `cabPedidoVta.Fecha`), si es
  anterior a hoy y de hace como mucho `DIAS_MAXIMOS_ATRAS` (15). Cómo: `ServicioPrecios.ParaDia(dia)` (instancia, sin estáticos);
  el motor de precios y las sugerencias siguen con hoy. Regalos por importe con día: por día inclusive.
- **Un día de margen (Black Friday 2026, Carlos 18/11/26)**: para esos pedidos de la tienda (mismo usuario permitido), lo que acabó
  el día D vale todavía para un pedido del D+1 (`VigenciaPedidoTienda.DIAS_MARGEN_TIENDA` = 1; regla `VigenteParaPedidoTienda` /
  `Vigencia.EsVigente(desde, hasta, dia, diasMargenFin)`): `vigencia-campanas` la retira hacia la 1:00 y la tienda tarda un par de
  horas en actualizarse. Por eso el día del pedido cuenta también si es HOY (`ParaDia(dia, diasMargenFin)`). Solo por el FINAL: lo
  que aún no ha empezado no vale. Vendedores (Nesto, NestoApp) y pedidos que no son de la tienda: acaba el día D, como siempre.

### Oferta N+M que además admite el precio especial (Novedades 1263, 13/11/26; continuación de #589)
- La regla general NO cambia: un N+M exige cobrar a precio de ficha y sin descuento (`ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa`).
  Excepción por DATO: `OfertasPermitidas.AcumulaPrecioEspecial` = 1 (fuera del EDMX, `Scripts/Novedades1263_OfertaAcumulaPrecioEspecial.sql`;
  sin la columna, ninguna acumula) en una oferta PROPIA del producto (no vale en la de la familia). `OfertasQueAcumulanPrecioEspecial`
  (caché de 5 min) + `ReglasOfertasPermitidas.PrecioEspecialAdmitido` (pura): el suelo del precio pasa a ser el precio especial vigente
  del producto para ese cliente; ni por debajo ni con descuento. Primer caso: packs 45929/45930/45931, 6+1 del 12 al 16/11/26 (feria online).
- `GET api/PlantillaVentas/CargarPrecio` admite `cantidadOferta` (opcional): con unidades de oferta, `aplicarDescuento` = false y la oferta
  marcada, devuelve el precio especial sin descuento (`AplicarPrecioEspecialQueAcumulaConLaOferta`). Nesto y NestoApp solo mandan ese dato.
  Las sugerencias de N+M siguen exigiendo precio de ficha (no proponen el 6+1 en un producto con precio especial).

### Campaña en bloque (Black Friday 2026, 20/11/26)
- `POST api/Campanas/Bloque?ensayo=true&incluirLineas=false` (`[Authorize]`, como crear campañas; ensayo por defecto: no escribe nada) y
  `POST api/Campanas/Bloque/Excel` (siempre ensayo; hojas «Productos» con TODAS las líneas y «Resumen»). Cuerpo `PeticionCampanaBloqueDTO`
  {Nombre ≤50, FechaDesde, FechaHasta (las dos obligatorias), Empresa (solo 1), Audiencia (0/1/2, por defecto de reglas y excepciones),
  Reglas [{Familia, Descuento, Audiencia?, ProductosExcluidos, TextosExcluidos, GruposExcluidos, SubgruposExcluidos («OUT» o «COS/OUT»),
  CondicionContado?}], Excepciones [{Producto, Descuento, Audiencia?, CondicionContado?}], ProductosExcluidos, DesbloquearAplicarDto,
  CondicionContado, TextoAvisoContado}. Descuentos en TANTO POR UNO (0 < d < 1). Crea filas POR PRODUCTO de DescuentosProducto con Campana.
- LA REGLA (pura) `ReglaCampanaBloque.Calcular` (`Infraestructure/Campanas/CampanaBloque.cs`): ya estaba (fila de ESTA campaña) · NoExiste ·
  EstadoNoVivo (null o < 0) · MateriaPrima (MTP) · Muestra (MMP) · ExcluidoPorReferencia · (solo por regla, no excepción) ExcluidoPorGrupo /
  Subgrupo / Texto (sin mayúsculas ni tildes, palabra entera) · SinPvp · FichaBloqueada («Aplicar Dto» = 0 sin desbloqueo) · solape. La
  excepción manda sobre su regla. Solape = otra fila de tarifa de producto (sin cliente/proveedor, desde 1 ud.) que se solapa en fechas:
  mejor o igual (precio efectivo) → fuera `YaTieneDescuentoMejorOIgual`; peor → se crea (Carlos 20/11/26, «queda el mejor de los dos»),
  `Solape` = true; nunca se borra nada. OJO: el motor no desempata dos filas de producto de la misma CantidadMínima (FirstOrDefault de SQL).
- De verdad: UNA transacción (`RepositorioCampanaBloqueSql`, applock `CampanaBloque:{empresa}:{campaña}`): SqlBulkCopy a una temporal + un
  INSERT … NOT EXISTS por tabla (DescuentosProducto, `CampanasAplicarDto` con las fichas a desbloquear, `CampanasCondicionContado`) y UNA
  fila en Modificaciones (Tabla `CampanaBloque`). Idempotente. Tienda: si aún no ha empezado no encola nada (lo hace `vigencia-campanas` el
  día que empieza); si ya corre, una vez los productos creados con audiencia > 0. Sin las tablas que hagan falta → 503.
- Baja por nombre (`DELETE api/Campanas/PorNombre/{n}` y `…/Cerrar`): por Nº Orden en una sentencia (ya no EF fila a fila), en la misma
  transacción el desbloqueo (lo no activado se borra; lo activado se cierra ayer y el job devuelve la ficha) y el contado; rastro
  `CampanaBorrada`/`CampanaCerrada`; solo encola lo que puede estar en la tienda (`ReglaCampanaBloque.PuedeEstarEnLaTienda`: ya empezada
  y acabada hace ≤ 2 días). Script `Scripts/BlackFriday2026_CampanasBloque.sql` (GRANT INSERT/DELETE/UPDATE en las dos tablas).
- **Distribuidores: la campaña ENCIMA del suyo (Carlos 20/11/26)**: marca por DATO `dbo.CampanasAcumulanDistribuidores` (Campana, FechaDesde,
  FechaHasta; `Scripts/BlackFriday2026_CampanasAcumulanDistribuidores.sql`; caché 5 min; sin ella, nada cambia). La rellena el alta en bloque con
  `AcumulaParaDistribuidores` (misma transacción) y la quitan la baja (borra) y el cierre (FechaHasta). LA REGLA `DescuentoDistribuidorCampana`
  (pura): `1 − (1 − propio) × (1 − campaña)` a 4 decimales (30+20 → 44 %; 50+25 → 62,5 %); propio = lo que el motor da SIN la fila de tarifa del
  producto (si el cliente tiene descuento propio del producto, ese); con PRECIO ESPECIAL propio no se compone; nunca peor que hoy (máximo). Solo
  la fila de campaña de PRODUCTO. Distribuidor = LA REGLA `ClientesDistribuidores` (algún contacto con estado 6 o 67; la usan también los cheques
  regalo; caché 10 min por cliente). Motor: `GestorPrecios.ConCampanaDeDistribuidor` tras el descuento propio del producto (primero la marca, sin
  ella ni consulta); validación: `ValidadorDescuentosPermitidos.DescuentoCompuestoDistribuidor` autoriza hasta el compuesto (propio = la mayor
  de las demás filas autorizadas). La tienda no cambia (no viaja nada de esto).
- **Una fila de campaña autoriza, pero no es un techo (Carlos 25/11/26, caso Israel, cliente 206, Lisap 43165)**: en `ValidadorDescuentosPermitidos`
  LA REGLA (pura) `ReglaDescuentosQuePrevalecen.Filtrar`: la fila específica del producto prevalece sobre familia/grupo/cliente (es la forma de
  marcar un techo) SOLO si alguna aplicable por cantidad es PERMANENTE (sin `Campana`); entonces cuentan las del producto (permanentes y de campaña:
  la campaña sube el techo esos días). Si las únicas del producto son de campaña, cuentan TODAS: la campaña suma permiso y no tapa el 52 % pactado
  por familia o grupo. Vale también para el precio autorizado; el compuesto de distribuidores no cambia. El motor de precios no se toca.
  (`ValidadorOfertasYDescuentosPermitidos` tiene la lógica vieja, pero no se usa en ningún sitio.)
- **Solo autorizan los niveles del motor (Carlos 25/11/26)**: `ServicioPrecios.BuscarDescuentosPermitidos` sigue leyendo con la consulta AMPLIA
  (familia O grupo) y después LA REGLA (pura) `ReglaFilaAutorizaProducto.Casa/Filtrar` deja solo: fila de producto; global de familia (sin filtro),
  familia+grupo (grupo de la ficha) o familia+grupo+subgrupo (#467, el Outlet: contra la categoría principal o una secundaria); del cliente: grupo
  (sin familia), familia, familia+grupo, familia+filtro. Una GLOBAL de solo grupo NO autoriza («tiene que ser una fila con ese cliente, con ese grupo
  y el producto a NULL»), ni una global de familia con filtro. Antes, Schwarzkop|PEL|OUT 50 % autorizaba el 50 % en todo PEL (695 líneas 15/09-24/11).
  Interruptor ParametrosUsuario «(defecto)» `Validacion.DescuentosCriteriosEstrictos` (sin fila o «1» = estricto; «0» = la consulta amplia de antes;
  sin crear filas, caché 1 min: `InterruptorDescuentosCriteriosEstrictos`). Afecta a todos los que usan esa lectura (validador de descuentos, precio
  especial admitido de ValidadorOfertasPermitidas y de PlantillaVentas, que solo miran filas de producto).
- **El redondeo a céntimos no inventa descuento (Laura 26/11/26, pedido de la tienda con el 22450)**: `MontarOfertaPedido` redondea el precio medio
  a 2 decimales (`precioCalculado`) y con él `descuentoReal` de un producto barato sale mayor del que es (1,67 € con el 10 % = 1,503 € → 1,50 →
  «10,18 %», y la fila de campaña del 10 % no lo autorizaba; 145 de las 5.151 filas del Black Friday estaban igual). Salió al encender los criterios
  estrictos: antes lo tapaba una fila amplia. Ahora `PrecioDescuentoProducto.precioSinRedondear` / `descuentoRealSinRedondear` y
  `ValidadorDescuentosPermitidos` da por bueno el MENOR de los dos descuentos (redondeado como siempre o exacto): nunca más estricto que antes.
  El voucher de la tienda (`ValidadorDescuentoTiendaOnline`, 5 % y 15 % exactos) y el precio autorizado no cambian.
- **Solo en stock (NestoAPI#889, Manuel 23/11/26; aviso NO estricto, como el del contado)**: `CondicionStock` en la petición (y `bool?` en regla y
  excepción) → una fila por producto en `dbo.CampanasCondicionStock` (Campana, Empresa, Producto, FechaDesde, FechaHasta; `Scripts/BlackFriday2026_CampanasCondicionStock.sql`;
  sin ella, ni aviso y el alta con la marca da 503), en la misma transacción; relanzar el alta con la marca sobre una campaña ya creada solo
  añade estas filas; la baja y el cierre por nombre también las quitan/cierran. Caché `CampanasCondicionStock` (10 min, ELMAH una vez). LA REGLA
  (pura) `AvisoCampanaSoloEnStock.Avisar` (`CondicionesCampana/`): líneas de producto -1..1 con DescuentoProducto o DescuentoLinea > 0 de un producto
  con fila vigente hoy, sumadas por producto; disponible (Carlos 24/11/26) = ExtractoProducto ALG+REI+ALC de las empresas 1 y 3 + lo que va de
  camino entre sedes (PreExtrProducto con traspaso) − LinPedidoVta -1..1 de OTROS pedidos (1 y 3) en esos almacenes, en UNA consulta
  (`LectorDisponibleCampanaStockSql`, solo si alguna línea es de la campaña). No llega → aviso `CampanaSoloEnStock` («El descuento de Black Friday
  del 45929 (NOMBRE) es solo para lo que haya en stock: hoy hay 2 de 5.») en `AvisosGrabacion` (POST), `Avisos` (PUT) y el correo. Nunca
  bloquea, quita el descuento ni rompe el guardado. Ni presupuestos ni la tienda online.
  **Pregunta antes de guardar (Carlos 24/11/26, como el contado)**: `POST api/PedidosVenta/AvisoCampanaSoloEnStock` (`PedidosVentaCondicionesCampanaController`,
  cuerpo = el `PedidoVentaDTO` que se va a guardar; solo lectura, siempre 200, nunca lanza) → {HayAviso, Campana, Mensaje (de usted, sin la pregunta final),
  Productos [{Producto, Nombre, Pedidas, Disponibles, ConDescuento, SinDescuento, DescuentoCampana, DescuentoSinCampana, PrecioSinCampana, Texto}]}.
  Al guardar, `PedidoVentaDTO.CampanaSoloEnStock` (solo de entrada, POST y PUT): null = como antes (aviso después); «QuitarDescuento» = ANTES de validar,
  LA REGLA pura `ReglaCampanaSoloEnStock.Ajustar` (`CampanaSoloEnStockPregunta.cs`): por producto, las unidades con descuento se limitan a lo disponible
  (por orden de línea; las intocables primero) y el resto va «sin campaña» en una línea NUEVA (`LineaPedidoVentaDTO.CopiaParaPartir`) o en la propia si
  no queda ninguna con descuento (Precio, DescuentoProducto, AplicarDescuento del motor y DescuentoLinea a 0); aviso `CampanaSoloEnStockAplicado` y no el de
  después; en el PUT solo líneas pendientes (-1) sin picking. «MantenerDescuento» = tal cual, sin aviso. «Sin campaña» = el motor con
  `PrecioDescuentoProducto.campanaExcluida` (las filas de tarifa del producto de esa campaña no cuentan; sin el dato, la consulta de siempre); un
  distribuidor sale con su descuento propio, sin componer. Una ficha que la campaña desbloquea (`CampanasAplicarDto` sin restaurar, vigente o activada, sin «Aplicar Dto» = 1 de antes;
  `ReglaCampanaSoloEnStock.FichaDesbloqueadaPorLaCampana`, una lectura por tanda) va «sin campaña» sin descuento (`aplicarDescuento` false). Una línea que ya va a precio «sin campaña» o peor no cuenta (lo partido no se vuelve a preguntar).

### «Ofertas para ti»: las ofertas de una campaña para cada cliente de la app y su push (25/11/26)
- Vale para CUALQUIER campaña (filas de tarifa por producto con `Campana`), no solo el Black Friday. Todo en `Infraestructure/Campanas/OfertasParaTi/`.
  LA REGLA (pura) `ReglaOfertasParaTi`: candidata = fila vigente hoy (`Vigencia`), audiencia 1 o 2 (lo que la tienda publica al profesional; el 0 no se
  anuncia), Estado ≥ 0, ni MMP ni MTP, con descuento (precio fijo → 1 − Precio/PVP); un producto en dos campañas, la de más. Por cliente: fuera lo
  pendiente (-1..1) y lo comprado desde el inicio de su campaña; orden 1) habituales que le tocan (≥ 2 días en 12 meses y ≥ 60 % del ciclo, mediana),
  2) habituales, 3) de sus 5 familias de más importe en 24 meses (descuento, popularidad), 4) lo demás (popularidad, descuento); como mucho 2 por
  familia entre los N si hay alternativas. `Motivo` LoSuelesPedir/TeToca/DeTusMarcas/TePuedeInteresar y `Texto` de tú. Stock = el lector de «solo en
  stock» (#889, pedido 0) por tramos y solo lo que hace falta.
- **El ranking de cada cliente (Carlos 25/11/26: «lo más vendido de la campaña es muy genérico»)**: con los criterios de las sugerencias de la
  plantilla, NUNCA con popularidad global de relleno. `IFuentesSugerenciasOfertas` (`FuentesSugerenciasOfertasSql`, el repositorio de las sugerencias,
  sin consultas nuevas): comprado junto (pares del job con lo comprado en 12 meses como «carrito», `ReglasSugerencias.CompradoJunto`), parecidos
  (afinidad item-kNN) y su perfil (grupo/subgrupo, la base de «a potenciar»), restringidos a la campaña. LA REGLA `ReglaRankingOfertasParaTi`
  (`SelectorOfertasConPrecio.cs`): grupos 1 le toca, 2 habitual, 3 comprado junto («Quienes piden X suelen pedir también este», `CompradoJunto`),
  4 parecido («Lo piden profesionales que compran lo mismo que tú», `Parecido`; la afinidad no guarda el producto suyo), 5 sus marcas, 6 su perfil
  («Lo piden profesionales como tú», `TePuedeInteresar`), 7 ninguna. Al modelo 1 (`IPuntuadorReposicion`) van las de sus fuentes y, hasta 300
  (`MAXIMO_PUNTUADOS`), las demás por popularidad (un cliente sin historial se ordena por lo que el modelo sabe de su perfil, #683). Orden: lo
  puntuado por probabilidad, detrás sus fuentes sin puntuar por la regla, al final el grupo 7. «Personal» = de sus fuentes o puntuada: solo eso va
  a la franja y a la push; sin modelo ni fuentes, `HayOfertas` false. El ranking y los precios ya calculados se guardan con el contexto de la
  campaña (`ContextoOfertasParaTi.Rankings`, `ResultadoOfertasCliente.Precios`; 10 min en `CacheContextoOfertasParaTi`); el stock se mira al recorrer.
- **Selección DESPUÉS del precio (caso 15191, 255 descuentos propios: veía UNA oferta)**: `SelectorOfertasConPrecio.Recorrer` recorre el ranking,
  mira el stock por tandas de 20, calcula el precio SOLO de lo que hace falta (precio a precio: el motor no calcula por tandas) y salta lo que no
  mejora (`ReglaPreciosOfertaParaTi.Mejora`). Franja y push: hasta N con diversidad (2 por familia si hay alternativas, `ReglaSeleccionOfertasParaTi.Acumulador`),
  60 consultas de precio y 200 productos mirados como mucho; 1 o 2 que mejoren, esas; ninguna, `HayOfertas` false + `Mensaje` «Ahora mismo no hay
  ofertas que mejoren tus precios habituales.». Contrato aditivo: `Total`, `Mensaje`, `HayMasOfertas` (pinta «Ver todas las ofertas del Black Friday»).
- `GET api/Pedidos/Cliente/Ofertas?campana=&maximo=5` (`OfertasClienteController`; cliente del claim, sin él 401; siempre 200): `Precio` por el camino de la
  app (`ProductosController.GetProductoPrecioReal`, 1 ud.) y `PrecioAntes` con `campanaExcluida` (`CalculadorPrecioSinCampanaMotor`, ficha desbloqueada
  sin descuento). Cargo 30 sin importes, cargo 31 con la tarifa en `Precio` (`ReglaPreciosOfertaParaTi`).
- `GET api/Pedidos/Cliente/Ofertas/Todas?campana=&desde=0&cuantas=20&familia=&texto=` (mismo controlador y reglas): el ranking ENTERO (también el grupo 7)
  filtrado por familia (código o nombre) y texto (nombre o referencia, sin tildes ni mayúsculas, `ReglaRankingOfertasParaTi.PasaFiltro`), desde la
  posición `desde`, hasta `cuantas` (1..50) que mejoran, sin diversidad, como mucho 150 productos mirados por petición → {Campana, Titulo, FechaHasta,
  TextoHasta, PrecioOculto, DescuentoOculto, Desde, Siguiente (null = no hay más), HayMas, TotalCampana, Familias (solo con desde 0), Ofertas}.
- `POST api/Campanas/Push?ensayo=true` (`CampanasPushController`, Dirección/Informática, 403 de usted; ensayo por defecto: solo lee, síncrono; DE VERDAD:
  202 {JobId, Campana, Mensaje} y el envío en Hangfire, `CampanaPushJobs.Enviar` sin reintentos ni concurrencia, con el resultado en Modificaciones
  Tabla `CampanaPush` (cifras, `ClientesEnviados`, `SaltadosDetalle`, `ConsultasPrecio`, `ProductosPuntuados`, `Segundos`); la petición y el horario se
  comprueban antes de encolar, `ServicioPushCampana.ValidarParaEnviar`). Destinatarios (`FuentePushCampanaSql.FiltroDestinatarios`): NestoTiendas activo
  con `RecibirConsejos` y Cliente, Empresa «1» O NULL (la app no la manda: los 60 dispositivos la tienen NULL y la push no encontraba a nadie;
  25/11/26), sin baja «Avisos de reposición» (las bajas van siempre con la empresa 1), SoloClientes/ExcluirClientes; sin repetir la campaña en
  `DiasSinRepetir` días mirando el BUZÓN (Datos con `"campana":"…"`). Las ofertas, la MISMA selección que la franja; `IncluirSinOfertas` por defecto
  FALSE (saltado «No hay ofertas que le mejoren el precio.»). El cuerpo solo nombra lo suyo (todo lo que no es «TePuedeInteresar»: «Producto 3
  (−30 %) y 1 más pensados para ti.»); si todo es «TePuedeInteresar», «Mira las ofertas del Black Friday que hemos elegido para ti.». Datos: SIEMPRE
  `tipo` «ofertas», `campana` e `imagenUrl`; NUNCA `productos` (los push de campaña no tocan el carrito). De verdad solo de 9 a 21, por `EnviarConsejoACliente`.
- ⚠️ `RecordatorioReposicionPushJobsService.ClientesConConsejos` tiene el MISMO filtro (`d.Empresa == EMPRESA`) y por eso el recordatorio de reposición
  no ha llegado nunca a ningún cliente: se deja así A PROPÓSITO (`// PENDIENTE (25/11/26)`) hasta que la versión nueva de la app esté en producción
  (con la de hoy esos avisos llenan el carrito).
- **Registro en sombra para reentrenar (Carlos 25/11/26)**: `dbo.OfertasClienteMostradas` (fuera del EDMX, `Scripts/OfertasClienteMostradas.sql`; sin
  ella todo igual y nada se registra, ELMAH una vez): una fila por oferta SERVIDA y cliente, día y origen («Cinco», «Todas», «Push»; índice único,
  `ReglaRegistroOfertas.Deduplicar`) con Posicion, PosicionRanking, Probabilidad, Motivo, VersionModelo, Descuento y Precio, insertada en segundo
  plano (`RegistroOfertasMostradas`, ADO con Enlist=false) al servir la franja, cada página de «Ver todas» y la push DE VERDAD que sale. `POST
  api/Pedidos/Cliente/Ofertas/Eventos` {Campana, Eventos [{Producto, Accion Vista|Abierta|Anadida, Cantidad, Origen Cinco|Todas}]} (cliente del JWT;
  SIEMPRE 204; 100 como mucho): `ReglaRegistroOfertas.Aplicar` sobre la fila del día (o una nueva sin Posicion): VistaEl/AbiertaEl/AnadidaEl la
  primera vez, CantidadAnadida se suma. Job `ofertas-cliente-compras` 3:35 Madrid (cola «indicadores», `CompraOfertasMostradasJobsService`):
  CompradaEl/PedidoCompra con el primer pedido del producto (no presupuesto) del día enseñado a los 7 siguientes, una consulta por tanda de 500,
  idempotente. `GET api/Campanas/Ofertas/Resultado?campana=&dias=30` (Dirección/Informática; 503 sin tabla): Total, PorOrigen, PorMotivo y PorDecil
  (deciles de probabilidad de más a menos) con servidas, vistas, abiertas, añadidas, compradas y sus porcentajes. Nada se entrena todavía.

### Descuentos pactados de un cliente por la API (Carlos 26/11/26; caso 35253, Lisap Keraplant al 50 %, hasta hoy por script de sa)
- `POST api/Clientes/{empresa}/{cliente}/DescuentosPactados?ensayo=true` {Familia?, Producto?, GrupoProducto?, FiltroProducto?, Descuento (TANTO POR UNO,
  0 < d < 1) | Precio, CantidadMinima (1)} → {Ensayo, Creada, Fila, ProductosAfectados, Ejemplo {Producto, Nombre, Pvp, PrecioConDescuento,
  DescuentoEfectivo}, Mensaje, Avisos}. UNA fila de DescuentosProducto del cliente (sin contacto, proveedor, subgrupo, fechas ni campaña; AudienciaOferta 0).
  LA REGLA (pura) `ReglasCondicionesCliente` (`Infraestructure/Clientes/CondicionesCliente.cs`): solo las combinaciones que el motor mira para un cliente
  (producto —con descuento o precio fijo—, grupo, familia, familia + grupo, familia + filtro; el precio fijo solo con producto) → 400 de usted si no.
  Misma clave (producto, familia, grupo, filtro y cantidad mínima, sin mayúsculas ni espacios) con el mismo valor → 200 `Creada` false; con otro valor →
  409 `DESCUENTO_PACTADO_YA_EXISTE` (bórrela primero). Ensayo (por defecto): a cuántos productos vivos aplica (Estado ≥ 0, familia y grupo exactos, nombre
  que EMPIEZA por el filtro, como el motor) y el precio del primero con SOLO esta fila (el motor puede encontrar otra mejor); de verdad con 0 productos → 400.
  De verdad: INSERT … NOT EXISTS (UPDLOCK) + Modificaciones (Tabla `DescuentoPactado`, JSON de la fila) en una transacción.
- `GET …/DescuentosPactados` (todas las filas del cliente, también las de contacto o campaña) y `DELETE …/DescuentosPactados/{numeroOrden}` (solo una fila
  de ESE cliente, 404 si no; rastro con la fila borrada). Crear y borrar: Dirección e Informática (son condiciones comerciales; 403 de usted a los demás);
  leer: además Administración. Cliente de la tienda, 403. Sin script. La tienda no cambia (las filas de cliente no viajan).

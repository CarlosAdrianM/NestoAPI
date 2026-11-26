# Reparto propio (agencia 14)

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Furgoneta y bultos, paradas, recogidas, notas de cobro, envíos internos, fin de ruta, día de reparto.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Furgoneta bulto a bulto (NestoAPI#772, 31/10/26)
- Cada bulto del reparto propio (`RP{envío}-{N}`, `NR{pedido}-{N}`, `R{traspaso}-{N}`) tiene su historia en `RepartoBultosMovimientos`
  (`Scripts/Issue772_FurgonetaBultos.sql`): Cargado, Recogido, Entregado, Descargado (Origen Repartidor/Almacen). «Lo que lleva encima»
  = último movimiento (por FechaHora del aparato e Id) Cargado o Recogido suyo (`NucleoFurgoneta`). `Cargar` sigue escribiendo en
  `EnviosAgenciaCargaBultos`/`RepartoParadasReposicionBultos` y además su Cargado con la MISMA clave (las NR no: el bulto no existe hasta recogerlo).
- `GET api/RepartoPropio/Furgoneta`, `POST Recoger`, `POST Descargar`, `POST Jornada/Terminar` (`ServicioRepartoPropio.Furgoneta.cs`);
  `BultosEntregados` en Entregar (opcional; sin leer, salen los de la parada que llevaba; obligatorio con `RepartoEscaneoEntregaObligatorio`="1" en «(defecto)»).
- Recoger el ÚLTIMO bulto R de una reposición de tienda → `MarcarRecogida` (como `POST Reposiciones/Recogida`) y `haCambiado` al grupo
  `Reposiciones:{tienda}` del AlmacenHub (Nesto, Enviar reposición). `GET Reposiciones/Puntual/Estado` trae BultosRecogidos/BultosTotal/RepartidorRecogida.
- Red de seguridad (`RedSeguridadFurgoneta`): recibir una REPO y el abono de una NR cierran lo que siga en una furgoneta. Job
  `furgoneta-sin-descargar` L-V 22:00 (campana a Almacén, Dirección y el repartidor). `GET api/Reposiciones/{traspaso}/Seguimiento`.
- **Sin etiqueta RP para recogidas y cobros (NestoAPI#847, 11/11/26)**: `ImprimirEtiqueta` y la reimpresión de `Tramitar` (la de Nesto)
  dan 409 con el motivo si el envío de la 14 es la parada de una nota de Recogida o de Cobro (`EtiquetaRepartoPropioDeNota`); sus
  bultos son NR{pedido}-{N}. Reimprimir la RP de una recogida dejó un bulto fantasma en la furgoneta. Si no se sabe el tipo, se imprime.
- **Entrega incompleta (NestoAPI#880 corte 1, 19/11/26)**: `EntregaIncompletaReparto.Evaluar` (pura, `FurgonetaBultos.cs`) clasifica cada bulto
  1..Bultos al entregar: Leido, EntregadoSinLeer (lo llevaba quien entrega), NoLeidoSeQueda (`EntregaRepartoDTO.SoloLeidos` = true con algún leído:
  NO se cierra, sigue en su furgoneta), NoSeCargo (sin carga vigente ni movimiento), VolvioAlAlmacen (Descargado), LoLlevaOtro. Si falta alguno y hay
  datos (algo cargado o leído; sin leer nada al cargar ni al entregar NO cuenta), el envío queda Entregado con DetalleEstado «Entregado (reparto propio):
  4 de 5 bultos. Falta RP…-5» (`PREFIJO_DETALLE`, `EsIncompleta`) + historia «Bultos» en la misma transacción; respuesta `BultosSinEntregar`,
  `BultosQueFaltan` {Codigo, Bulto, Situacion, Motivo}, `ResumenEntregaIncompleta` (también en el reintento); UN aviso tras el OK
  (`IAvisosEntregaIncompleta`: campana a Almacén + campana/push a quien hizo el pedido y su vendedor, tipo `EntregaIncompletaReparto`). El job de las
  22:00 no cierra los bultos de un envío incompleto. Ni recogidas, ni NC, ni reposiciones. `EntregaIncompleta` (+ `DetalleEntregaIncompleta`) en los
  DTO de envío: Nesto lo marca en Agencias › Tramitados («Incompleto», cabecera) y en la pestaña Entrega del pedido.
- **Bultos pendientes con cierre a mano (NestoAPI#880 corte 2, 26/11/26)**: cada bulto que FALTA en una entrega incompleta queda Pendiente en
  `dbo.RepartoBultosPendientes` (`Scripts/Issue880_RepartoBultosPendientes.sql`, fuera del EDMX, CHECK de Estado/Cierre/Situación y coherencia; sin él no se apunta
  nada y los endpoints dan 503), desde `CompletarEntregaIncompleta` (también en el reintento; único por Codigo; nunca rompe la entrega). Todo en `BultosPendientesReparto.cs`
  (LAS REGLAS `ReglasBultosPendientes`, `ServicioBultosPendientesReparto`, `RepositorioBultosPendientesSql`). `GET api/RepartoPropio/BultosPendientes?soloPendientes=true&empresa=
  &pedido=&envio=&dias=30` (`BultosPendientesRepartoController`; empleados y vendedores, 403 al cliente de la tienda; 500 como mucho) → [{Id, Empresa, EnvioId, Pedido, Cliente,
  Contacto, Nombre, Codigo, Bulto, Total, SituacionAlEntregar, MotivoFalta, Repartidor, FechaEntrega, Estado Pendiente|Cerrado, Cierre, MotivoCierre, UsuarioCierre, FechaCierre,
  DondeEsta (último movimiento de la furgoneta: «En la furgoneta de Miguel desde el 26/11 a las 9:42.», «En el almacén: lo descargó…»), Texto}]. `POST …/BultosPendientes/{id}/Cerrar`
  {Cierre EntregadoEnMano|Perdido|Abonado, Motivo 1..500} (Almacén, Dirección, Informática, `PermisosRepartoPropio.PuedeCerrarBultoPendiente`; 400/403/404/409 de usted): UPDATE
  condicionado a Pendiente + rastro `RepartoBultoPendienteCerrado` y, con EntregadoEnMano, el movimiento Entregado del bulto (Origen Almacen, «Entregado en mano: motivo», clave
  derivada del Id) en la MISMA transacción. Job `reparto-bultos-pendientes` L-V 9:00 Madrid (no en festivo de Algete): campana `BultosPendientesReparto` a Almacén. Seguimiento
  público: «Entregado parcialmente» (con «contacte con nosotros») mientras el envío esté Entregado incompleto y le quede algún Pendiente (`ReglasBultosPendientes.EntregaParcial`;
  si no se puede contar, se fía del DetalleEstado). Ni envíos internos (EI), ni recogidas, ni NC. Las entregas incompletas de antes del script no se apuntan.
- **Reembolso entero o autorizado (Carlos 19/11/26)**: en `Entregar` con Efectivo e `ImporteCobrado` < reembolso − 0,01 € hace falta la MISMA
  autorización que para no cobrar (Autorizada o Telefonica, #773; vale para cualquier importe); sin ella, 409 `Codigo` REEMBOLSO_PARCIAL (en la raíz
  y en error.code), regla pura `ReglaReembolsoEntero`. Cobrar de más no cambia. Las notas de cobro NO (lo de menos queda a cuenta). La petición de
  autorización admite `ImporteCobrado` opcional (sin columna: va en el Motivo; si Ariadna ya lo escribe, no se repite).
- **Cobrar MÁS que el reembolso, con confirmación (Carlos 26/11/26)**: en `Entregar` con Efectivo e `ImporteCobrado` > reembolso + 0,01 € → 409
  `{ error: { code, message, details: { reembolso, importeCobrado, deMas } }, Message, Codigo: "COBRO_DE_MAS" }` «Está cobrando 30,00 € más que el reembolso
  (120,00 €). Si es correcto, confírmelo: lo de más quedará a cuenta del cliente.» salvo `EntregaRepartoDTO.ConfirmarCobroDeMas` = true (opcional); confirmado,
  se contabiliza lo cobrado como hasta ahora. LA REGLA `ReglaReembolsoEntero.PideConfirmarDeMas`. Va DESPUÉS de la idempotencia: un reintento con la misma clave
  no se frena. Ni notas de cobro, ni enlace ni tarjeta. Ariadna: al recibir COBRO_DE_MAS, preguntar y reenviar con `ConfirmarCobroDeMas` = true (misma clave).
- **Bultos en la prueba de entrega (19/11/26)**: `PruebaEntregaDTO`/`PruebaEntregaEnvioDTO` traen (aditivo, solo reparto propio) `Bultos`
  [{Codigo, Bulto, Situacion, Entregado, Texto}] y `ResumenBultos` («Leídos 6 de 7. Sin leer: RP…-7.»), con la misma regla de #880 sobre el envío ya
  entregado (`EntregaIncompletaReparto.EvaluarEntregado`). Sin movimientos RP del envío (antiguos, otras agencias), null. Nesto lo pinta en una línea.
- **Packing entre dos PDA (incidencia 1169, Ariadna)**: F8 en la lista no vale para una entrega que ha terminado OTRA PDA y solo está
  resaltada (se reimprimía el envío del compañero y la propia no salía); vale al entrar en ella o elegirla con las flechas.
- **Incidencias 1189/1190 (11/11/26)**: cargar la parada de una RECOGIDA (o de una NC) ya no apunta Cargado RP (no hay bulto; la carga en
  `EnviosAgenciaCargaBultos` sigue: repartidor, soloMios, SinCargar igual). Una incidencia de un envío sin bultos en ninguna furgoneta
  apunta su Cargado (salió sin leer). Descargar admite el papel `NE{pedido}` (todos los RP de su envío en furgoneta) y con una NR cierra
  el RP sin bulto de su recogida. `SinLeer` = true en Descargar: quita de SU furgoneta un bulto de GET Furgoneta (409 `EN_OTRA_FURGONETA`
  si es de otro), rastro en Modificaciones (`FurgonetaDescargaSinLeer`) y campana a Almacén. La respuesta trae `Descargados`.
- **Entregado cierra el envío (caso 251099, Carlos 19/11/26)**: Entregar un ENVÍO apunta Entregado a los leídos y también a los demás RP
  de ese envío que lleve encima QUIEN ENTREGA (`CierreEntregaFurgoneta`): Descripcion con « (entregado sin leer)» y UNA fila en Modificaciones
  `FurgonetaEntregaSinLeer` (también al no leer ninguno). Los que lleve otro repartidor y las paradas de reposición no cambian. Lectura
  obligatoria = como hoy, al menos uno. Job de las 22:00: antes de avisar, los RP de envíos de la 14 ya en Estado 2 que sigan en una furgoneta
  se cierran igual (Origen Almacen, usuario el repartidor, rastro por envío) y no cuentan para el aviso; si no puede mirar el estado, avisa como siempre.
- **Solo lo cargado y carga obligatoria (punto 6, Carlos 20/11/26)**: una parada es de un repartidor solo cuando ha leído algo suyo (RP, o el papel
  NE/NR; las NC nacen asignadas). `GET Envios?soloMios=true&soloCargados=true` = SOLO las suyas (LA REGLA `NucleoParadasReparto.Visible(…, soloCargados)`;
  sin el parámetro, las suyas + las de nadie, como siempre). `SoloCargados` (opcional) en OrdenSugerido/Estimacion (ya eran solo las suyas).
  `POST Carga/Terminar` {Fecha?} → {SinCargar[{EnvioId, CodigoDocumento, Nombre, Poblacion, Bultos, BultosSinCargar}], Total, Mensaje}: lo de «Sin cargar»
  que no lleva NADIE; campana `RepartoSinCargar` a Almacén, una vez cada 30 min por conjunto (en memoria, `MemoriaAvisosSinCargar`). Interruptor «(defecto)»
  `Reparto.CargaObligatoria` (solo «1»): Entregar un envío o nota de entrega/recogida que no lleva quien entrega → 409 `NO_CARGADA`, salvo que en
  BultosEntregados venga algo de ESA parada: se carga a su nombre (`CargarEnvio` con Reasignar, clave derivada, mismo rastro) y sigue
  (`ReglaCargaObligatoria.Decidir`, `CargaObligatoriaReparto.cs`). Ni NC ni reposiciones; un reintento con la misma clave, tampoco.

### Manda el bulto: el candado de oficina y las lecturas rechazadas (Carlos 20/11/26)
- Un bulto del reparto propio está siempre en uno de: almacén sin cargar · furgoneta de un repartidor · entregado · de vuelta en el almacén. LA REGLA
  (pura) `CandadoFurgonetaEnvio.QuienLoLleva` (`CandadoFurgonetaEnvio.cs`): algún RP del envío (o el papel NR de su nota) cuyo último movimiento es
  Cargado o Recogido → quién y desde cuándo (núcleo `EnFurgoneta`, que usan también los envíos internos). Envíos 251169 (cargado 9:42, «cerrado a mano»
  9:43) y 251227 (borrado con la etiqueta pegada).
- En la 14, desde oficina, 409 `{ error, Message, Codigo: "EN_FURGONETA", Repartidor, Desde }` («No se puede: el envío 251169 lo lleva Andre en la
  furgoneta desde las 9:42. Que lo descargue o lo entregue primero.»): DarPorCerrado, Anular, DELETE y el PUT / ModificarDatos que cambie la agencia o
  pase a 2, 4 o 5 (`CambioQueFrena`). Sin el script de la furgoneta o si no se puede leer, se sigue (ELMAH). Lo del repartidor (Entregar, Incidencia,
  Descargar, Recoger) y los jobs no pasan por aquí. «Dar por cerrado» en la 14: SIEMPRE 409 `CIERRE_NO_PERMITIDO` (las recogidas «no recogidas» y
  «anuladas» van por RecogidasParadaReparto y no cambian). Modificar (dirección) no cambia agencia ni estado: sin candado.
- DELETE api/EnviosAgencias/{id} deja SIEMPRE rastro (Modificaciones, Tabla `EnvioAgenciaBorrado`: las columnas del envío y la historia que se borra con
  él, `RastroBorradoEnvio`) en la MISMA transacción, de cualquier agencia y estado. (El borrado de un reenvío que sobra, `AnularPorEntregaAsync`, y el de
  las etiquetas pendientes al borrar un pedido no pasan por aquí.)
- `POST api/EnviosAgencias/{id}/Nota` {Texto 1..500} (Almacén, Administración, Dirección, Informática; 403 de usted): una línea en EnviosHistoria (Campo
  «Nota») sin tocar el envío. 200 {Numero, Texto, Usuario, Fecha}; 404.
- Lecturas rechazadas: `dbo.RepartoLecturasRechazadas` (`Scripts/RepartoLecturasRechazadas.sql`). Punto ÚNICO: el filtro `[ApuntarLecturasRechazadas]` de
  RepartoPropioController apunta en segundo plano (nunca rompe ni retrasa) todo 4xx de GetBulto, Cargar, Entregar, Incidencia, Recoger, Descargar y
  NoRecogida (`ReglaLecturasRechazadas`: código leído o EnvioId, Status, Codigo del 409, Mensaje 500, X-Dispositivo, X-Ariadna-Version). Sin la tabla, nada
  (ELMAH una vez). `GET api/RepartoPropio/LecturasRechazadas?dias=2&usuario=` (Almacén, Dirección, Informática; 500 como mucho; 503 sin el script).

### Envíos internos entre sedes (sugerencia 1277, Alfredo, 20/11/26)
- Lo que va de una sede a otra y NO es stock (café, folios, aparatos de demostración o de clientes): ni almacenes, ni ExtractoProducto, ni
  cliente/pedido ficticio. `dbo.RepartoEnviosInternos` (`Scripts/RepartoEnviosInternos.sql`, que además amplía la CHECK de TipoCodigo de
  RepartoBultosMovimientos con 'EI'; sin él, endpoints 503 y el reparto como antes). Bultos `EI{id}-{n}` con su historia en RepartoBultosMovimientos
  (TipoCodigo EI, EnvioId = la parada de entrega). Estado guardado Pendiente/Entregado/Anulado; EnCamino se deduce (algún bulto movido).
- LAS REGLAS (puras) `ReglasEnviosInternos` (`EnviosInternosReparto.cs`): sedes ALG/REI/ALC distintas, 1..50 bultos, descripción ≤200; paradas con EnvioId
  ≤ −1.000.000.000 (entrega −(1e9+2·id), recogida −(1e9+2·id+1); las de reposición, −Id, quedan por encima): RECOGIDA en el origen si no es Algete mientras
  quede algún bulto allí (de nadie hasta que alguien recoge uno), ENTREGA en el destino mientras esté vivo; `DondeEsta` por bulto; minutos de parada = reposición.
- Oficina (`ServicioEnviosInternos`): `POST api/RepartoPropio/EnviosInternos` {Origen, Destino, Descripcion, Bultos, Impresora?} → 201 {Id, Origen, Destino,
  Descripcion, Bultos, Estado, Codigos, EtiquetasImpresas} (etiqueta 4x6 de la plantilla del reparto propio, «ENVÍO INTERNO», en la impresora pedida o la
  ImpresoraAgencia del usuario; si no se puede, se crea igual); `POST …/{id}/ImprimirEtiquetas` {Impresora?}; `DELETE …/{id}?motivo=` (409 EN_FURGONETA
  con el cuerpo del candado; rastro `EnvioInternoAnulado`). Alta, impresión y anulación: Almacén, Dirección, Informática. `GET …/EnviosInternos?dias=7&soloVivos=true`
  (grupos del reparto) con `BultosDetalle` (Situacion SinCargar/EnFurgoneta/Entregado/Descargado y Texto).
- Reparto (`ServicioRepartoPropio.EnviosInternos.cs`): GET Envios las saca con `TipoParada` «EnvioInterno», `EsEnvioInterno`, `CodigoDocumento` EI{id},
  `FaseEnvioInterno` Recogida/Entrega, `SedeOrigen`, `SedeDestino`, `SedeParada`, `DescripcionEnvioInterno` y la dirección/coordenadas de la sede. GET Bulto/EI…:
  la de recogida si el bulto sigue en el origen, si no la de entrega. Cargar (origen Algete o bulto descargado), Recoger (origen tienda), Descargar y Entregar
  los de siempre con el código EI; Entregar exige el nombre de quien lo recibe (sin cobro, firma ni DNI), BultosEntregados opcional, «solo lo cargado»
  (`ReglaCargaObligatoria`) y entrega incompleta con el núcleo común `EntregaIncompletaReparto.EvaluarCodigos`. Sin Incidencia (400).

### Fin previsto de ruta del reparto propio (NestoAPI#881, 19/11/26)
- LA REGLA de los minutos por parada: `MinutosParadaReparto` (pura, `TiemposParadaReparto.cs`): envío y nota de entrega 15, nota de cobro 10,
  recogida 15, reposición 25; con servicio técnico, los tecleados en la nota, si no los del tipo, si no 60. Misma dirección (mismo cliente+contacto
  o mismas coordenadas a 4 decimales) = una vez (la mayor, en la primera). Colchón +15 % sobre los minutos del motor; descarga en Algete 15.
  Cierre: `HorarioCierreReparto` (L-J 17:00, V 15:00; sábado, domingo y `EsFestivoRepartoPropio` = sin cierre). Todo cambiable con
  ParametrosUsuario «(defecto)» `Reparto.MinutosParada*`, `Reparto.MinutosServicioTecnico`, `Reparto.ColchonConduccion` («15»),
  `Reparto.MinutosDescargaAlgete`, `Reparto.CierreLunesAJueves`/`CierreViernes` («17:00»), sin script (sin fila = el código; caché 5 min).
  Sustituye a los 5 min fijos: las llegadas y el «fuera de horario preferible» de OrdenSugerido (#763/#769) usan la misma regla y el colchón.
- `POST api/RepartoPropio/Estimacion` (misma petición que OrdenSugerido): evalúa el orden VIGENTE de las pendientes del repartidor, sin
  reordenar ni guardar → `EstimacionFinRutaDTO` (FinEstimado, LlegadaDestino, MinutosConduccion con colchón, MinutosParadas, MinutosDescarga, Km,
  HoraCierre, SeComparaConCierre, PasaDelCierre, MinutosDeMas, Aviso «Fin previsto 17:40 · 40 min después del cierre», Aproximada, Paradas[]
  con LlegadaEstimada y MinutosParada; además `MinutosRestantes` desde `CalculadoEl` y `HoraCierreDelDia` «17:00»: la PDA pinta ahora + MinutosRestantes). OrdenSugerido trae lo mismo en `FinRuta` (aditivo) y `MinutosParada` en cada parada. Cálculo puro
  `CalculoFinRuta`; servicio `ServicioRepartoPropio.FinRuta.cs`. Destino personalizado (casa) → solo se compara con el cierre si lleva algo para
  Algete (recogida pendiente en su ruta o NR/R «Recogido» en su furgoneta; la caja NO se mira) y entonces pasa por Algete. Sin coordenadas: centro
  del CP (`dbo.CentrosCodigosPostales`); sin OSRM: línea recta × 1,3 a 30 km/h. Nunca falla: sale `Aproximada`.
- **Servicio técnico (corte 2)**: `dbo.RepartoTiposServicioTecnico` (empieza vacía) y `NotasEntregaCabecera.TipoServicioTecnicoId`/`MinutosServicio`
  (`Scripts/Issue881_RepartoFinRutaYTiempos.sql`; sin él: tipos vacíos, POST/PUT 503, paradas sin servicio técnico). `GET/POST api/RepartoPropio/TiposServicioTecnico`
  (nombre repetido → el que hay), `GET/PUT api/NotasEntrega/{empresa}/{pedido}/ServicioTecnico` {TipoServicioTecnicoId | TipoServicioTecnicoNombre (crea el
  tipo), MinutosServicio 1..600; sin tipo = quitar; rastro `NotaServicioTecnico` en Modificaciones}. Al crear: `PedidoVentaDTO.tipoServicioTecnicoId/
  tipoServicioTecnicoNombre/minutosServicio` (solo POST; se valida antes y se guarda después) y los mismos campos en Confirmar una recogida solicitada.
  `GET Envios`: `ServicioTecnico` y `MinutosServicio` (tecleados, del tipo o 60) en las notas; entran en el fin de ruta. `ServicioServicioTecnicoReparto`.
- **Tiempos reales y modelo en sombra (corte 3)**: campos opcionales de la PDA: `FechaHoraLlegada` y `FechaHoraIr` en Entregar, Incidencia y Recoger
  (cuando cierra la reposición de una tienda); `Latitud`/`Longitud` en Cargar (al movimiento Cargado) y en Jornada/Terminar (`RepartoJornadas`, columnas
  opcionales). Tras cada OK, `dbo.RepartoParadasTiempos` (una fila por parada cerrada, idempotente por la clave; la inserción pone la parada ANTERIOR
  del repartidor ese día y su posición; `MinutosPrevistos` = la regla) y cada Estimacion/OrdenSugerido deja una foto en `dbo.RepartoEstimaciones`
  (una por repartidor cada 10 min; si no, actualiza). Nunca rompen (ELMAH). `ServicioRepartoPropio.Tiempos.cs`, `TiemposRealesReparto.cs` (ADO).
  Modelo (`RepartoPropio/ModeloTiemposParada/`, patrón ModeloPlazos): objetivo = (fin − fin anterior) − conducción en línea recta con colchón (fuera
  la primera del día y los intervalos > 120 min); segundo registro fin − llegada. Rasgos: tipo de parada, servicio técnico, bultos, cobro y forma, CP3,
  hora, día, repartidor, minutos tecleados. < 200 ejemplos → «sin datos suficientes». Job `reparto-tiempos-parada-modelo` 4:25 Madrid (cola
  «entrenamiento», nunca en horario): valida las últimas 4 semanas contra la REGLA y apunta SIEMPRE en `dbo.RepartoTiemposModeloEntrenamientos`;
  guarda `tiempos_parada.zip` en ModeloLlamadas:Carpeta si no empeora. Interruptor «(defecto)» `Reparto.TiempoParadaQuienManda` = «Regla» | «Modelo»
  (con «Modelo» el fin de ruta usa el modelo; `MinutosDe` en la respuesta). `GET api/RepartoPropio/TiemposParada/Comparacion` (Dirección/Informática).
- **Aviso de paradas para recalcular el fin de ruta (19/11/26)**: `repartoCambiado` Motivo «Parada» (grupo «Reparto», Dispositivo «NestoAPI»: sin
  excluir PDA) cuando las paradas cambian por algo que NO hace el repartidor. LA REGLA (pura) `CambiosParadasReparto.AfectaAlReparto` (parada = 14 en
  Tramitado/Incidentado): nace (alta tramitada o paso a Tramitado), deja de serlo (borrado, pendiente/en curso, cerrado, otra agencia) o cambia
  día (fecha), reembolso o bultos; NO entregar ni incidencia (el repartidor ya avisa). Se mira en `NVEntities.SaveChanges` (todo lo de EF: tramitar,
  TramitarTodos, ConfirmarTramitacion, DesdePedido/packing, Facturar rutas, notas de cobro, aplazar, anular, reembolso desfasado…) y en el PUT de
  EnviosAgencias (adjunta la entidad: compara con lo registrado); también el PUT del servicio técnico de una nota. Punto único
  `AvisoParadasReparto.Avisar` → `AgrupadorAvisosParadas`: UN aviso tras 3 s sin más (máx. 20 s), en segundo plano, nunca lanza. Las paradas de
  reposición solo las crea/cierra el repartidor (Carga/Entrega): sin aviso nuevo. Lo que Nesto escriba en EnviosAgencia por su cuenta no avisa.
- **Informe por repartidor (corte 4, 20/11/26)**: `GET api/RepartoPropio/Informe?desde=&hasta=&repartidor=&soloMio=` (`InformeRepartoController`; Almacén,
  Dirección e Informática; sin fechas, los 7 últimos días; ≤ 366 días; sin `RepartoParadasTiempos`, 503). Solo lectura en CUATRO consultas (`RepositorioInformeReparto`:
  paradas del corte 3, movimientos de bultos agregados por usuario y día, «Terminar jornada» y la PRIMERA estimación del día; las tres últimas opcionales → `Aviso`).
  LA REGLA pura `CalculoInformeReparto` (`InformeReparto.cs`): paradas hechas = entregadas + recogidas; incidencias aparte; horas = primera carga (o primer
  movimiento) → último movimiento (bultos, paradas, jornada), sin descontar la comida; horas en ruta = primera → última parada cerrada; dinero = efectivo y
  tarjeta (enlace/tarjeta) de los reembolsos + otros cobros (notas de cobro); minutos de parada reales = fin − llegada (0..120) frente a `MinutosPrevistos`.
  Totales y medias por día de la semana (por repartidor y de todos) y desglose por CP y día. Km null (corte 5). Sin PDF/Excel, ni cobros de deuda (POST Cobros: no están en estas tablas).

### Día de reparto de nuestra ruta: código postal y excepción por contacto (NestoAPI#764, #799)
- Las rutas propias (`RutasPropiasConfiguradas`: AT, 16…) tienen día de reparto: el del CP de entrega en `CodigosPostalesDiaReparto`
  (`DiasRepartoCodigosPostales`) o, si el contacto tiene excepción, el de `ClientesDiaReparto` (`DiasRepartoClientes`, #799,
  `Scripts/Issue799_ClientesDiaReparto.sql`). Ambas fuera del EDMX, caché de 10 min, sin tabla = sin días.
- LA REGLA: `DiasRepartoCodigosPostales.DiaRutaInterna(ruta, cp, empresa, cliente, contacto)` (la excepción manda). Úsala siempre que
  se conozca el cliente (fecha de entrega, FechaEntregaAgencia, fechas futuras, aviso de cierre, picking, sugerencia de ruta).
- `GET/PUT api/Clientes/DiaReparto` (PUT: Dirección/Almacén/Administración; Ruta null quita): invalida la caché y recalcula las líneas
  pendientes sin picking del contacto por ruta propia al día nuevo de ESA semana (`RepositorioDiasRepartoClientes.NuevaFechaEntrega`).

### Nota de cobro: parada de solo cobro (NestoAPI#786, 04/11/26)
- `NotasEntregaCabecera.Tipo` = «Cobro» (`TiposNotaEntrega.COBRO`; `Scripts/Issue786_NotasCobro.sql`: CHECK ampliada, columna
  `ImporteCobro` y tabla `NotasEntregaCobroEfectos` con los Nº Orden de ExtractoCliente en su orden). Pedido con NotaEntrega = 1 **sin
  líneas**: no entra en picking, packing ni Facturar rutas. No se crea ni se cambia por POST PedidosVenta / PUT Cabecera (400).
- `POST api/NotasEntrega/Cobro` (`ServicioNotasCobro`) `{ Cliente, Contacto?, Efectos?, Importe?, Fecha?, Comentario? }` → 201
  `NotaCobroDTO`; en la MISMA transacción crea la parada: envío de la agencia 14 ya tramitado, Bultos 0, Reembolso = importe (la suma de
  lo pendiente si no se da). Cualquier cliente, sea cual sea la ruta de su ficha (Carlos 13/11/26: si Almacén o Administración mandan a cobrar, es por algo); 409 si el contacto ya tiene otra en el reparto. Avisa `repartoCambiado` (Motivo «Parada»).
- En `GET api/RepartoPropio/Envios`: `TipoParada` «NotaCobro», `EsNotaCobro`, `CodigoDocumento` NC{pedido}, `EfectosCobro` (pendiente de
  ahora) y `EmpresaCobro` de los efectos (uno de la 3 → sin tarjeta). No va en la furgoneta, ni en SinCargar, ni se leen bultos al entregar.
- Entregar: efectivo → `ServicioCobrosReparto.Cobrar` (#766) con sus efectos y la MISMA clave que la entrega (idempotente; antes de tocar
  el envío), asiento y caja en la prueba; tarjeta → el enlace del envío lleva los efectos (`LiquidacionNotaCobro`: suman justo el importe,
  si no, a cuenta); sin cobro → autorización (#773). Firma: `TextoConformidadEntrega.Cobro` («he pagado X €»). Gemelas: Ariadna (PDA) y Nesto (pantalla).
- **Repartidor asignado (Carlos 04/11/26)**: `Repartidor` obligatorio en el POST (400 si no es de `GET api/RepartoPropio/Repartidores`:
  sin grupo de seguridad de reparto, son los que han movido o cargado bultos de la furgoneta en 90 días + el que lo pide,
  `ServicioRepartidores`). Se guarda en `NotasEntregaCabecera.Repartidor` (`Scripts/Issue786b_NotaCobroRepartidor.sql`; sin él, 400 al
  crear). En `GET Envios` la NC lleva ese `Repartidor` (no se carga: `CompletarCarga` no lo borra) y con soloMios solo le sale a él;
  las NC de antes (sin repartidor) siguen saliendo a todos. Ariadna no filtra: lo hace la API.

### Cobrar una deuda: cobro repetido o entero a cuenta (POST api/RepartoPropio/Cobros, #766; caso 9251 del 20/11/26, Carlos 26/11/26)
- Dos cobros en un minuto al mismo cliente y el segundo quedó entero «a cuenta». LA REGLA (pura) `ReglaCobroRepetidoReparto`
  (`CobroDeudaReparto.cs`), SOLO en POST Cobros (`ServicioCobrosReparto.CobrosRecientes`, lo pone el controlador; la nota de cobro, dentro de
  la entrega, no se frena): sin `Confirmar` = true (campo nuevo del cuerpo), (1) el cliente ya tiene un cobro del reparto en los últimos
  `MINUTOS_REPETIDO` (30) con OTRA clave (Modificaciones Tabla «CobroReparto», Fecha del registro, JSON con Cliente; incluye los de notas
  de cobro) → 409 `COBRO_REPETIDO` («Ya ha cobrado 84,80 € a este cliente a las 12:31. Si es otro cobro distinto, confírmelo.»; de otro
  repartidor, «Andre ya ha cobrado…»; details {Importe, Fecha, Usuario}); (2) no liquida ningún efecto (sin efectos o todos ya no
  pendientes) → 409 `COBRO_TODO_A_CUENTA` («Este cobro no corresponde a ninguna deuda del cliente: quedaría entero a cuenta. Confírmelo
  si es correcto.»; details {Importe}). Cuerpo del 409 = el de siempre del reparto: {error {code, message, details}, Message, Codigo}.
  Nada se contabiliza. El reintento con la MISMA clave devuelve lo de antes (va antes). Ariadna vieja (sin el campo) recibe el 409: querido.

### Recogida solicitada por el vendedor (NestoAPI#855, 12/11/26; gemelas Nesto#635 y NestoApp#259)
- `dbo.RecogidasSolicitadas` (fuera del EDMX, `Scripts/Issue855_RecogidasSolicitadas.sql`; sin él, 503 y el job no hace nada). Estado
  guardado Solicitada/Confirmada/Rechazada/Anulada; al leer se calcula EnReparto (parada de la 14 tramitada/incidentada) y Recogida
  (abono hecho, parada entregada o bultos NR recogidos). `QuienRecoge`: Reparto (solo ruta propia, `RutasRepartoPropio` + interruptor;
  400 «pídala como la recojo yo») o Vendedor (cualquier contacto).
- `POST/GET api/NotasEntrega/Recogidas/Solicitudes` (vendedor de NestoApp solo sus clientes, `PermisosRecogidasSolicitadas`; empleados),
  `GET …/Solicitudes/{id}`, `GET …/Pendientes`, `POST …/{id}/Confirmar` y `…/{id}/Rechazar` (los cuatro grupos de notas), `DELETE …/{id}?motivo=`.
- Confirmar crea la nota de recogida como #757 (pedido NotaEntrega = 1, líneas de texto de 50, NotasEntregaCabecera Recogida) en UNA
  transacción con la solicitud (`RepositorioRecogidasSolicitadas.Confirmar`). Con Vendedor las líneas nacen con fecha 31/12/2099: no
  entran en picking ni Facturar rutas (no hay parada) y `POST Recogidas/Abono` (la lectura del NR en Algete) no le pide la parada
  recogida (`ServicioAbonosRecogida.EsRecogidaDelVendedor`). Ese es el cierre en los dos casos; Descargar de Ariadna no vale (409 NO_EN_FURGONETA).
- Anular: quien la pidió (o los grupos de notas) mientras no esté en marcha (Solicitada, o Confirmada sin picking, envío ni abono: se borra
  la nota como Borrar pedido); en marcha, solo Almacén o Administración: se marca y la nota se queda (`AvisoAnulacion`, campana a Almacén).
- Avisos: campana a Almacén (`RecogidaSolicitada`), push NestoApp + campana Nesto a quien la pidió (`RecogidaSolicitadaResuelta`,
  `RecogidaSolicitadaAnulada`), `haCambiado` al grupo `RecogidasSolicitadas` del AlmacenHub. Job `recogidas-solicitadas-sin-respuesta`
  (L-V cada hora 8-20): un día laborable de Algete sin contestar → `RecogidaSolicitadaSinRespuesta` a Almacén y a quien la pidió, una vez.
- Al abonarla (`POST Recogidas/Abono`, ya guardado el abono; `ServicioRecogidasSolicitadas.AlAbonar`, nunca lo rompe): con Vendedor, su
  nota se procesa como nota de entrega (`ServicioNotasEntrega.ProcesarNotaRecogidaDelVendedor`: líneas a -2 con fecha de hoy, SIN
  ExtractoRuta; `RepositorioRecogidasSolicitadas.CerrarNotaDelVendedor`, una transacción, idempotente: si falla, ELMAH y se reintenta en
  la siguiente lectura del NR). Con abono nuevo, `RecogidaSolicitadaRecibida` (push + campana, Datos + `estado` «Recogida») a quien la
  pidió. Las notas de recogida sin solicitud y `ProcesarNotaEntrega` (Facturar rutas) no cambian.

### Recogida imprevista del repartidor (NestoAPI#859, 12/11/26; gemela Ariadna 29d1390)
- `POST api/RepartoPropio/RecogidasImprevistas/Comprobar` {EnvioParada? | Empresa?, Cliente, Contacto} y `POST api/RepartoPropio/RecogidasImprevistas`
  {Clave (Guid), FechaHora, …, Bultos 1-99, Texto ≤500, FotoBase64?, AceptaRetornoVivo} (`ServicioRecogidasImprevistas`, permisos de reparto).
  Retorno vivo = envío de otra agencia (no la 14) del contacto con Retorno ≠ 0, Tramitado o Incidentado, de los últimos 60 días, tras refrescar su
  seguimiento (4 s por envío; si no contesta, el estado guardado y se dice). Con retorno vivo sin aceptar: 409 `{Message, Codigo: "RETORNO_VIVO", RetornosVivos}`.
- Crear, en UNA transacción (`RepositorioRecogidasSolicitadas.CrearImprevista`, mismo núcleo `CrearNota` que Confirmar de #855): la nota NR con
  líneas a 31/12/2099 (sin picking ni parada), fila «Confirmada» con Origen «Repartidor», Clave y AvisoUrgente (`Scripts/Issue859_RecogidasImprevistas.sql`;
  sin él, 503) y los «Recogido» NR{p}-{n} en la furgoneta de quien llama. Misma Clave → la misma respuesta. Se cierra como la del vendedor al leer
  el NR (o NR-n) en Algete (`EsRecogidaDelVendedor` incluye Origen «Repartidor»). Foto opcional al blob de bultos (`recogidas-imprevistas/…`).
- Avisos: campana `RecogidaImprevista` a Almacén, haCambiado a Reparto y RecogidasSolicitadas; con retorno aceptado, urgente
  `RecogidaConRetornoEnCamino` (referencia = el envío; suena mientras siga vivo) y línea en EnviosHistoria. Nunca se toca la agencia.

### Parada de una nota de recogida: «No se ha recogido», comentario y «Anular recogida» (punto 5, Carlos 19/11/26)
- `POST api/RepartoPropio/Envios/{id}/NoRecogida` {ClaveIdempotencia, FechaHora?, Motivo (obligatorio, ≤500), Latitud?, Longitud?, FechaHoraLlegada?,
  FechaHoraIr?}: solo notas de recogida; la parada sale del reparto como Cerrado a mano (5), DetalleEstado «No recogido (reparto propio): motivo»,
  prueba Incidencia (Motivo Otro, la CHECK no cambia), sin bultos NR; 200 {EnvioId, Estado «NoRecogido»}; 409 `RECOGIDA_YA_RECOGIDA` con bultos NR en
  una furgoneta o abonada. `EntregaRepartoDTO.Comentario` (≤500, solo recogidas): historia, DetalleEstado «Recogido (reparto propio): …» y prueba
  (`PruebaEntregaDTO.Observaciones`). Con solicitud (#855): aviso a quien la pidió (`RecogidaSolicitadaNoRecogida`/`RecogidaSolicitadaComentario`).
- `POST api/RepartoPropio/Recogidas/{pedido}/Anular` {Motivo} (grupos del reparto): parada en reparto → Cerrado a mano «Recogida anulada: motivo», historia y
  Modificaciones `RecogidaAnulada` en UNA transacción (`RecogidasParadaRepartoSql.CerrarParada`, solo si sigue 1/3); la solicitud, Anulada. Las líneas de
  la nota no se tocan. Reglas puras `ReglasParadaRecogida` (`RecogidasParadaReparto.cs`).
- **Anular una recogida marcada «Recogido» por error (caso 930553, Carlos 26/11/26)**: el MISMO `POST …/Recogidas/{pedido}/Anular` {Motivo} vale también si la
  parada ya está Entregada (2), solo para Almacén y Dirección (`PermisosRepartoPropio.PuedeAnularRecogidaMarcada`; los demás grupos del reparto, 403 de
  usted) y si no hay abono de esa recogida (`NotasEntregaCabecera.PedidoAbono`, lo que mira `ServicioAbonosRecogida`; con abono, 409 `RECOGIDA_ABONADA`; si
  no se puede leer, 503). LA REGLA `ReglasParadaRecogida.MotivoNoAnulableMarcada`. Los bultos NR en una furgoneta NO lo impiden. En UNA transacción
  (`RecogidasParadaRepartoSql.AnularRecogidaMarcada`; el UPDATE exige Estado 2 y la nota sin abono con UPDLOCK: si cambia, 409): Cerrado a mano (5),
  DetalleEstado «Recogida anulada (marcada por error): motivo», historia, Modificaciones `RecogidaAnulada` (Nuevo con `MarcadaPorError`), la solicitud
  (#855) Confirmada → Anulada, y un Descargado (Origen Almacen, clave derivada) por cada NR{pedido}-{n} cuyo último movimiento es Cargado/Recogido.
  Después, el aviso de la solicitud anulada y `repartoCambiado` «Parada». Al quedar en 5, leer el NR en Algete ya no crea abono sin «Forzar».
  Las líneas de la nota no se tocan: ya están a -2 (Facturar rutas las procesó al crear la parada), así que ni picking ni Facturar rutas las vuelven a coger.
- **«No se ha recogido» pasa a Almacén, que decide (Carlos 20/11/26)**: con `dbo.RepartoRecogidasNoRecogidas` (`Scripts/RepartoRecogidasNoRecogidas.sql`;
  sin él, NoRecogida cierra como arriba y los endpoints nuevos dan 503) la recogida NO se cierra: el envío queda Incidentado (3) con el mismo DetalleEstado y
  FechaEntrega = `FechaRepartoPropio.FECHA_PENDIENTE_DECIDIR` (31/12/2099: no sale en ningún día, ni en SinCargar; leer su papel da «pendiente de que
  Almacén decida»), fila Pendiente (escrita ANTES que el envío; la lista exige el envío Incidentado de la 14) y campana `RecogidaNoRecogida` a Almacén.
  Contrato de NoRecogida igual. `GET api/RepartoPropio/Recogidas/NoRecogidas` → [{Pedido, CodigoDocumento, EnvioId, Cliente, Contacto, Nombre, Direccion,
  Poblacion, Motivo, Repartidor, FechaHora}]. `POST …/Recogidas/{pedido}/Reprogramar` {Fecha?} → {Pedido, CodigoDocumento, EnvioId, FechaEntrega, Mensaje}:
  la MISMA nota y parada vuelven a Tramitado ese día o, sin él, LA REGLA `ReglasParadaRecogida.FechaReprogramada` (desde mañana, su día de ruta interna
  `DiaRutaInterna` o el siguiente sin `EsFestivoRepartoPropio`); sin repartidor (sus cargas vigentes reciben un Descargado RP Origen Almacen), historia,
  Modificaciones `RecogidaReprogramada` en UNA transacción y `repartoCambiado` «Parada»; fecha pasada 400; no pendiente 409. `Anular` también la cierra
  (fila Anulada). Grupos del reparto (Almacén, Administración, Dirección, Informática).

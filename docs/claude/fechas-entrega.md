# Fechas de entrega del pedido

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Motor #606 de cara al cliente: promesa en riesgo, correo del pedido, dónde está lo que falta.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### «Dónde está lo que falta» (pedido 930942, 19/11/26)
- `GET api/PedidosVenta/{empresa}/{pedido}/DondeEstaLoQueFalta` (`DondeEstaLoQueFaltaController`, aparte de PedidosVentaController; `[Authorize]`, el
  cliente de la tienda con claim «cliente» → 403; 404 sin pedido). Por cada producto NO listo en Algete (la regla de la promesa en riesgo: todo lo
  que no es Picking ni Algete) su frase y datos (`DondeEstaLoQueFaltaDTO` → `Productos[].Texto/Situaciones[]`). Sale del motor #606
  (`CalcularPedido`, una sola verdad) y, para lo EnCamino, del seguimiento #772 por TANDA (`ServicioSeguimientoReposiciones.SeguimientosTanda`:
  cabeceras, bultos y movimientos en tres consultas para todos los traspasos; sin las cargas antiguas de las paradas). Si el seguimiento falla:
  ELMAH y la frase sin el detalle del repartidor. Todo en Algete: lista vacía y `Mensaje` con el picking. Textos puros en
  `ReglasDondeEstaLoQueFalta` («… (1 ud.): viene en la reposición 81463 de Alcobendas, recogida por Miguel el miércoles 18/11 a las 12:53; pendiente
  de recibir en Algete.»). Nesto: sección plegable en el detalle del pedido y «Dónde está» en «Pedidos con la fecha en riesgo».

### Color de las líneas del correo del pedido: el mismo motor que el pie (pedido 925901, 18/11/26)
- El correo pintaba cada línea con `GestorStocks.ColorStock` (stock − TODOS los pendientes) y el pie con el motor #606: un pedido a 31/12/2099
  ponía en rojo una línea que el pie daba «con stock en Algete». LA REGLA (pura): `ReglaColorLineaCorreo` (`Infraestructure/PedidosVenta/`): de
  las partidas del producto en `FechaEntregaAgencia.Desglose`, la PEOR: Picking/Algete → verde; Kit/Montaje sin componente que falte → rosa (no
  cuenta como «tiene que venir»: ni reserva ni parte el pedido); Tienda/EnCamino (o pack que espera un componente de tienda) → rosa «tiene que
  venir»; Proveedor/SinStock (o pack que espera eso) → rojo. `GestorPresupuestos.StockDeLinea` + `ResumenStock`: de ahí salen `faltaStockDeAlgo`
  (#507, «¡¡¡ ATENCIÓN !!!») y `tieneQueVenirAlgunProducto`. Reservas a tiendas y su copia (`CalcularReservasDesdeMotor`, `ObtenerAlmacenesConReservas`):
  lo que el motor trae de cada tienda (Tienda + Unidades).
- Respaldo `ColorStock` (como siempre) para lo que el motor no calcula: presupuestos, líneas que no son de Algete, servidas (≥ 2) o sin cantidad,
  producto que no está en el desglose, origen desconocido, o el motor falló (FechaEntregaAgencia null). El «antes» del «✓ LIBERAR» y su copia a
  tiendas siguen con la cuenta de siempre (no hay motor del pedido anterior). Sugeridor de modo de servicio y portes no cambian.

### Pie del correo: cuando manda la fecha de entrega del pedido (pedido 931105, 19/11/26)
- Si la fecha que se da es posterior a la que daría la mercancía SOLO por la fecha de entrega de las líneas, el motor lo dice: el mismo cálculo con las
  fechas de las líneas a «hoy» (en memoria, se restauran) sale antes → `ResultadoFechaEntregaAgencia.NoAntesDeFechaEntrega` / `FechaEntregaAgenciaDTO.NoAntesDeFechaEntrega`
  y la frase única `TextosCertezaFechaEntrega.FechaEntregaDelPedido` («No sale antes del jueves 26/11/2026, que es la fecha de entrega del pedido.») en el
  Motivo, en el pie del correo y en el Mensaje de «Dónde está lo que falta». Ninguna fecha cambia. La promesa en riesgo no la usa.

### Promesa en riesgo: frase corta, causa original y buena noticia (NestoAPI#693, Carlos 18/11/26)
- `TextosPromesaRiesgo.Motivo` (campana, pantalla de Nesto, que pinta `Motivo` tal cual, y correo): QUÉ FALTA, CUÁNDO LLEGA y CUÁNDO SALE, nada
  de lo que está en Algete. Agrupa misma causa y fecha («Faltan 45949 JABON CARBON ACTIVO y 5 productos más (50 uds.): llegan hoy del proveedor.
  Sale el jueves 19/11 (aproximado). Va todo junto.»); nombre a 30 letras por palabra; fechas SIEMPRE «el jueves 19/11» (nunca «hoy» ni
  «mañana»: la frase se guarda y se lee días después; año solo si es otro);
  certeza en una palabra (`TextosCertezaFechaEntrega.CalificativoCorto`: Segura y CasiSegura → seguro, Aproximada, Orientativa). Ya no lleva la
  causa del evento («Reina mandó 1 de 3…»: queda en `Evento`) ni «todo está listo en Algete, pero no salió…».
- Regla (`EvaluadorPromesaRiesgo.MereceAviso`): se avisa en el acto lo que tiene culpables (no listo en Algete) o fecha de entrega posterior.
  Registrado y ya sin culpables → `YaLlego`: «Faltaba X; llegó el martes 17/11. Sale el martes 17/11.», se resuelve con «Ha llegado lo que
  faltaba», campana UNA vez solo si se avisó la mala, y en el correo resumen en una línea solo si la mala salió en un correo (`LeerYaSalenSinCorreo`, 7 días).
- Todo en Algete y va tarde (sin registrar, o tras la buena noticia) → fila «en observación» (`EnObservacion` = 1, `TodoListoDesde`): sin
  aviso, fuera del listado y del correo; sigue siendo candidato. Si sale, se resuelve sin ruido. Si termina entero el primer día laborable de
  Algete posterior (`ObservacionVencida`, fines de semana y festivos) y sigue sin salir → `SigueSinSalir`: pasa a aviso normal («Está todo en
  Algete desde el lunes 16/11 y sigue sin salir. Es de prepago: compruebe que está pagado. Revise el pedido.»; causas baratas: prepago PRE,
  retenido por falta #858, fecha de entrega posterior, ruta propia; si no, «Revise el pedido (retenido, pendiente de prepago, fecha de
  entrega…).»). Una vez: que su fecha se corra un día cada día no es «empeorar»; si aparece un culpable, riesgo normal.
- Columnas opcionales `CausaOriginal` (JSON de lo que faltaba; se suma, nunca se pierde), `TodoListoDesde` y `EnObservacion`
  (`Scripts/PromesaRiesgo_CausaOriginal.sql`; la API las usa solo si están las tres; caché + ELMAH una vez). Sin ellas: textos nuevos, buena
  noticia «Ya está todo en Algete: sale en el próximo picking.» y sin aviso de los que lo tienen todo en Algete (como antes).
- **Lo que ya viene de camino va antes que lo libre de las tiendas (pedido 930942, 19/11/26)**: el repartidor del motor #606 daba lo que
  falta a lo LIBRE de una tienda antes que a la reposición ya terminada que viene a Algete (Alcobendas mandó la unidad en la 81463 y el aviso
  dijo «tiene que venir de Reina… lunes 23/11»). Ahora `RepartidorStockFechaEntregaAgencia.TomarEnCaminoAAlgete` (puro) coge primero lo de
  camino (`DatosSombraModoServicio.EnCaminoAAlgete`, `SQL_EN_CAMINO_A_ALGETE`: PreExtrProducto neto por empresa+traspaso+producto > 0 con la
  entrada en ALG y la tienda de su Delegación), descontando lo que ya deben en Algete los demás pedidos (la reposición más antigua es suya).
  La partida `EnCamino` lleva `TraspasoEnCamino`/`TiendaEnCamino` (NO `Tienda`: no se reserva nada en ella) y los textos dicen «viene de
  camino en la reposición 81463 de Alcobendas» (`CalculadoraFechaEntregaAgencia.DeCamino`). Correo resumen: solo va lo recalculado sin
  error en `RevisarAntesDelCorreo` (`PedidosRevisados`) o lo detectado después de empezar (`ReglaCorreoPromesaRiesgo.PuedeIr`); lo demás
  espera al siguiente correo, nunca sale el porqué de hace horas.

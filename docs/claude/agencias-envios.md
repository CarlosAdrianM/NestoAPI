# Agencias y envíos

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Seguimiento, retrasados, devueltos y reenvíos, prueba de entrega, picking de cada envío.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Agency and Shipping
- Multiple shipping agencies supported
- Special handling for Glovo (`GestorAgenciasGlovo`)
- Shipping cost tracking through specific accounts
- **Seguimiento automático y Retrasados (sugerencia 1329, 18/11/26)**: LA REGLA `ReglaSeguimientoAutomaticoAgencia`: una agencia tiene
  seguimiento automático si la consulta el job de seguimiento (`IFabricaAgenciasRemotas.AgenciasConSeguimiento`: perfil con
  `IPerfilConSeguimiento` + puerta de activas, sin cuarentena ni sombra; con perfil de seguimiento: GLS 1, Innovatrans 12 y CTT 13) o si su perfil declara
  `IPerfilConEstadosPropios` (el reparto propio 14: el repartidor cierra los envíos desde Ariadna). `GET api/EnviosAgencias/Retrasados`
  solo lista las de esa regla: Canteras (11), la GLS de la espejo (5), Glovo, Sending, CEX… no salen (se quedaban 60 días en Tramitado).
  Ninguna agencia escrita a mano: la que tenga perfil con seguimiento entra sola.
- **Reenvío que sobra (novedad 1366, 19/11/26; «cerrado a mano = entregado»)**: la anulación de #699 (borra el reenvío sin tramitar, Estado < 1,
  y la decisión pasa a EntregadoPorLaAgencia) también cuando el original queda Entregado o Cerrado a mano por una persona: entrega del repartidor
  (`ServicioRepartoPropio.ResolverReenvioSobrante`), «Dar por cerrado», PUT de estado y «Borrar envío» del reenvío (si el original ya está terminado,
  anula en vez de dar 409). LA REGLA: `ReenvioEntregadoPorLaAgencia.EstaTerminado`/`PasaATerminado` (el seguimiento sigue con `PasaAEntregado`).
  Campana `ReenvioAnulado` a quien pidió el reenvío.
- **Devueltos: quién decide (Carlos 19/11/26)**: `POST {id}/Reenviar` y `{id}/Abonar` solo quien hizo el pedido, su vendedor (claim «Vendedor» o
  usuario del vendedor) y Almacén, Dirección e Informática (Administración NO): `PermisosDevueltos` (mismo núcleo que RetencionPorFalta/Decidir), 403
  de usted. «Ya está aquí» (`HaVuelto`, las dos rutas): Almacén, Dirección e Informática. El GET de Devueltos sigue abierto a empleados y vendedores.
  Los GET de EnviosAgencias con datos personales (Pendientes, PorPedido, Tramitados, Historia, Propuesta, UltimoEnvioCliente…) dan 403 a un token de
  cliente de la tienda (`ValidadorAccesoCliente.EsClienteTienda`); la app del cliente usa PedidosClienteController.
- **Devueltos: `Resuelto` (sugerencia 1329)**: `EnvioDevueltoDTO.Resuelto` (solo lectura, `ReglaDevueltoResuelto`) = la caja ha vuelto
  (#870) + decisión tomada + (si es Reenviar) el reenvío ya tramitado; y siempre los `EntregadoPorLaAgencia` (Carlos 18/11/26: si la agencia acabó entregando, ya no se espera caja). La API devuelve todos; Nesto oculta los resueltos por defecto
  (casilla «Ver también los resueltos») y tiene una caja de lectura que llama a `POST api/EnviosAgencias/HaVuelto` {Codigo}.
- **Devueltos por pedido (sugerencia 1356, 19/11/26)**: `GET api/EnviosAgencias/Devueltos?empresa=&pedido=` filtra en la consulta (misma
  ventana de 60 días); sin `pedido`, como siempre. Lo usa la pestaña «Entrega» del pedido en Nesto. Reenviar/Abonar siguen con solo `[Authorize]`.

### Prueba de entrega de cualquier agencia (10/11/26)
- `GET api/EnviosAgencias/{id}/PruebaEntrega` (`PruebasEntregaController`, permisos de `PermisosRepartoPropio.PuedeVerDatosPersonales`):
  siempre 200 con `PruebaEntregaEnvioDTO` (Disponible false + Mensaje si no hay: agencia sin prueba, sin entregar, la agencia
  falla); 403 sin permiso, 404 si no existe. Documento en base64 + TipoDocumento, o UrlDocumento. Nunca se guarda copia: se pide
  a la agencia en cada consulta. `GET api/RepartoPropio/Envios/{id}/PruebaEntrega` y `…/Firma` siguen igual.
- `ServicioPruebasEntrega` resuelve el proveedor por la agencia en el registro de perfiles SIN puerta y convierte cualquier fallo
  en mensaje (ELMAH). Proveedores: `PruebaEntregaRepartoPropio` (la de Ariadna + firma PNG), `PruebaEntregaCTT` (`cls/pods/{código}`
  con hash MD5 de código + centro + CP destino; receptor del evento 2100 del seguimiento), `PruebaEntregaGls` (GetExpCli: pod,
  NombreEntrega, DniEntrega y la imagen de la digitalización «POD», que hoy es un JPEG y se trae desde el servidor).
- **Agencia nueva** = una clase `IProveedorPruebaEntrega` + `IPerfilConPruebaEntrega` en su perfil (`PerfilesAgencias.cs`). Ni el
  endpoint ni Nesto cambian (Nesto: `MostradorPruebaEntrega` en ControlesUsuario, en Agencias › Tramitados y en el detalle del pedido).

### Reenvíos: solo sale lo que ha vuelto (NestoAPI#870, 16/11/26)
- El reenvío de «Reenviar» (#699) nace en Pendientes (corte 1). La caja del original «ha vuelto» = fila en `dbo.EnviosAgenciaHaVuelto`
  (fuera del EDMX, `Scripts/Issue870_EnviosAgenciaHaVuelto.sql`; Envio, Fecha, Usuario, Como Lectura/YaEstaAqui/AlImprimir, Codigo) + línea
  `HaVuelto` en EnviosHistoria (`Infraestructure/Agencias/EnviosHaVuelto.cs`). Idempotente; se marca aunque no tenga reenvío; solo lo que ha salido (Estado >= 1).
- `POST api/EnviosAgencias/{id}/HaVuelto` (Nesto, «Ya está aquí» en Devueltos) y `POST api/EnviosAgencias/HaVuelto` {Codigo} (Ariadna, Entradas › F6
  «Ha vuelto un paquete»: CodigoBarras exacto o con 1-4 cifras de bulto al final, o el número de envío tecleado). 404/409 (NO_HA_SALIDO,
  VARIOS_ENVIOS)/503 sin el script. Con reenvío esperando: campana `EnvioHaVuelto` + push NestoApp a quien lo pidió (no si lo marca él ni forzado).
- LA GUARDA (`ServicioEnviosHaVuelto.EsperandoCajaAsync`): reenvío (Decision Reenviar, < tramitado) sin vuelta del original → 409 `ESPERANDO_CAJA`
  en PUT (pendiente→en curso/tramitado), Tramitar, ImprimirEtiqueta, EtiquetaZpl y ConfirmarTramitacion; `?yaEstaAqui=true` fuerza y marca
  (AlImprimir). TramitarTodos (GLS) lo salta (`Resultado` EsperandoCaja). La propuesta de envío no reutiliza ese pendiente. Sin las tablas o si
  falla la comprobación: no actúa (ELMAH). EntregadoPorLaAgencia sigue igual. Devueltos trae HaVuelto/FechaHaVuelto/UsuarioHaVuelto/ComoHaVuelto/ReenvioEsperandoCaja.

### Qué picking lleva cada envío (incidencia 1328, 18/11/26)
- `dbo.EnviosAgenciaPickings` (Envio, Picking, FechaAlta, Usuario; PK Envio+Picking; fuera del EDMX, `Scripts/Issue1328_EnviosAgenciaPickings.sql`;
  sin ella, todo como antes y ELMAH una vez). Un único punto: `RepositorioPickingsEnvio` (`Infraestructure/Agencias/PickingsEnvio.cs`, nunca lanza).
  Se apunta (unión, nunca se quita) con los pickings de las líneas del pedido en curso o en albarán (`ReglasPickingsEnvio.PickingsDeLasLineas`) al
  crear en curso por POST api/EnviosAgencias, en DesdePedido (nuevo, pendiente reutilizado, ampliación; también el packing de Ariadna) y en el PUT
  de un envío aún en el almacén que queda en curso o más (`ApuntarAlModificar`). El reenvío (#699) copia los del original; el retorno de
  GestionEnTransito lleva `SIN_MERCANCIA` (0). La etiqueta pendiente (estado -1), las paradas de notas y las notas de cobro no apuntan (sin dato).
- LA REGLA (`AplazamientoPedidoConPicking.YaLoTieneLaAgencia(envios, pickings, …)`, `EnvioDelPedido.PuedeLlevar`): la agencia «ya tiene» las
  líneas que cambian de fecha si un envío del pedido tramitado o más (no devuelto ni reparto propio por entregar) PUEDE llevarlas: con dato, si
  lleva alguno de sus pickings; sin dato, si no está terminado (entregado 2 o cerrado a mano 5: lo terminado no puede contener lo que sigue en el
  almacén). Lo mismo filtra las etiquetas a anular y los envíos del reparto propio a los que se mueve el día. La usan el PUT del pedido (por línea)
  y AvisarLogisticaAplazamiento. Caso 930358: el 250999 entregado (picking 100095) ya no bloquea aplazar el 100114.

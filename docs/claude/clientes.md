# Clientes

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Alta y direcciones, origen, embudo de Odoo, sugerencias de llamadas, app de clientes (TNV).
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Dirección 2 y número propio en el alta (NestoAPI#874 corte 1, Nesto#643, 17/11/26)
- `Clientes.Dirección` sigue siendo calle y número (50). Lo que necesita el repartidor va en `Clientes.Direccion2` (nvarchar(100)) +
  `TipoLugar` (Piso/Local/Casa/Nave/Otro, CHECK `CK_Clientes_TipoLugar`), fuera del EDMX (`Scripts/Issue874_ClientesDireccion2.sql`;
  `Direccion2ClienteSql`, sin columnas: no guarda ni lee y ELMAH una vez).
- LA REGLA (pura): `Direccion2Cliente.Componer` / `ParaDireccion(clienteCrear, exigir)`: Piso = piso y puerta («1º D»); Local = nº opcional
  + RÓTULO obligatorio sin valor por defecto («LOCAL 6 · RÓTULO: EL EDÉN»); Casa («CASA»); Nave = nº («NAVE 4»); Otro = texto. Mayúsculas.
  En el alta (`ClienteCrear.TipoLugar`, `Piso`, `Puerta`, `NumeroLocal`, `Rotulo`, `NumeroNave`, `OtroTexto`): con dirección y sin tipo → 400
  solo con ParametrosUsuario «(defecto)» `ExigirDireccion2Alta` = «1» (nace a «0» hasta NestoApp#286); con tipo se valida siempre.
  Pensada para que la use también la modificación cuando CAMBIE la dirección (Carlos 17/11/26; pendiente, corte 2).
- Número propio: si Places da la calle sin número, `ComprobarDatosGenerales(..., numero)` lo comprueba con Google (geocodificación calle +
  número + CP, `IServicioDireccionesGoogle.Geocodificar`; `NumeroPropioDireccion.Confirmado`: mismo portal y mismo CP) y lo devuelve en
  `DireccionFormateada`; «S/N» no se pregunta. En «Número» SOLO va el portal (cifras + letra/BIS opcionales, o S/N): «Nº 99», «99 local 6»,
  texto → 400 siempre. Válido que Google no encuentra (Carlos 17/11/26): no bloquea; la respuesta trae `NumeroSinConfirmarPorGoogle` +
  `AvisoNumero` (la pregunta) y el alta (`ClienteCrear.Numero`, que se vuelve a comprobar) solo sigue con `NumeroConfirmadoSinGoogle`
  (si no, 400 con la pregunta); entonces apunta una fila en Modificaciones (Tabla `ClienteNumeroSinGoogle`). Google caído en el alta → sigue.
- Envío: `DireccionEnvioCliente.Componer` (pura): Dirección + «, » + Dirección 2 si cabe en 80; si no, delante de las Observaciones (80) sin
  quitar nada; si tampoco, aviso. Núcleo: `GestorPedidoParaAgencia` (GET ParaAgencia de Nesto, propuesta del servidor, DesdePedido y packing;
  el aviso sale en `PropuestaEnvioDTO.AvisoDireccion2` y en los avisos de DesdePedido) + etiqueta pendiente y ActualizarDireccionEtiqueta.
- **A Odoo (corte 3, 18/11/26), SOLO de ida**: el mensaje de clientes (`ClienteSyncMessage`, uno por contacto) lleva `Direccion2` y `TipoLugar` al
  lado de `Direccion`, ausentes (WhenWritingNull) si el contacto no tiene, si están en blanco o sin el script; nunca "". Se leen en UNA consulta
  por cliente (`Direccion2ClienteSql.LeerDeClientes`, vía `IServicioGestorClientes.LeerDireccion2Clientes`; el gestor guarda las filas del último
  cliente para sus contactos seguidos y la publicación suelta las vuelve a leer). Cuándo viajan: en cualquier publicación del cliente; el alta
  escribe la Dirección 2 ANTES de publicar, así que su mensaje ya la lleva. `trgClientesUpd` NO encola por Direccion2/TipoLugar (hoy solo las
  escribe el alta). De Odoo a Nesto se ignoran: ni `ClienteChangeDetector` ni `BarreraClientesExternos` las miran.
- **En los correos del disparador (19/11/26, caso 42154/0)**: «Cambio de Vendedor» y «Cambio de Dirección» pintan «Dirección, Dirección 2»
  (`Scripts/Issue874_trgClientesUpd_Direccion2.sql`, vuelta atrás `…_ROLLBACK.sql`). En el alta la columna aún es NULL en el INSERT: antes de
  guardar la ficha, `Clientes.Direccion2EnSesion.Poner` (vía `GestorClientes.PonerDireccion2EnSesion`) deja la Dirección 2 en el contexto de la
  sesión (`sp_set_session_context`, clave `Direccion2Alta:{empresa}|{cliente}|{contacto}`) con la conexión abierta a mano (misma sesión que el
  INSERT; en el alta con entrega, la de la transacción) y la borra al acabar; el disparador usa esa clave antes que `i.Direccion2`. Nunca
  rompe el alta (ELMAH una vez). Para la modificación (corte 2) vale el mismo mecanismo: poner la nueva antes del SaveChanges.
- **Dirección de entrega distinta en la misma alta (19/11/26, Carlos)**: `ClienteCrear.DireccionEntrega` (`DireccionEntregaCrear`, opcional; sin
  él, el alta de siempre): Direccion, DireccionVerificada, Numero, NumeroConfirmadoSinGoogle, TipoLugar + sus campos, CodigoPostal, Poblacion,
  Provincia, Ruta, Telefono, ComentariosRuta, ComentariosPicking. Solo en un cliente NUEVO con dirección fiscal (`AltaConDireccionEntrega.MotivoNoAdmitida`:
  con EsContacto, sin dirección, sin CP → 400). La segunda dirección pasa por el MISMO núcleo que la fiscal (`GestorClientes.ValidarDireccionAlta`:
  #499, «¿Qué es?» y número propio) y su 400 empieza por «Dirección de entrega: ». El contacto de entrega es un alta de contacto de las de siempre
  (`AltaConDireccionEntrega.ContactoDeEntrega` + `PrepararClienteCrear` con el contacto siguiente reservado): hereda NIF, nombre, estado, forma y
  plazos, IBAN (CCC), vendedores, días de servir y las MISMAS personas de contacto (comunes); no los Comentarios generales ni el origen. En UNA
  transacción (`AbrirTransaccionAlta`; lo que lee por otra conexión se lee antes del primer guardado): principal, entrega y `ContactoDefecto` del
  principal = el de entrega (ContactoCobro/Bonificacion siguen en el principal). Se publican los dos a Odoo (primero el principal), cada uno con su
  Dirección 2. Respuesta: la ficha del principal + `ContactoEntrega` (solo si se creó). Un CP nuevo compartido por las dos se crea una vez.
  Nesto: casilla «La entrega es en otra dirección» en «Datos generales» (`BloqueDireccionAlta`, el mismo bloque que la fiscal). NestoApp: issue gemela.
- **CP del alta solo de Google (19/11/26)**: en Nesto, al CREAR, el código postal no se teclea nunca salvo que Google no lo dé (novedad 482); en la
  modificación sigue como estaba. La API NO comprueba en el POST que el CP case con la dirección de Google (solo la pantalla; NestoApp sin cambios).
- **Dirección completa visible (NestoAPI#879, novedad 1320, 19/11/26)**: LA REGLA (pura) `DireccionCompletaCliente.Componer` = Dirección + «, » +
  Dirección 2 (sin ella, la Dirección; SIN truncar: es para pantalla; la de la etiqueta sigue siendo `DireccionEnvioCliente`). Campo aditivo
  `direccionCompleta` (solo lectura, el PUT lo ignora; `direccion` no cambia) en `ClienteDTO` de GET api/Clientes (las tres búsquedas y el cliente
  por empresa/cliente/contacto) y en `DireccionesEntregaClienteDTO` de GET PlantillaVentas/DireccionesEntrega. Las listas leen la Dirección 2 en
  UNA consulta por empresa (`Direccion2ClienteSql.LeerDeClientes`, IN por lotes de 500); sin columnas o si falla, = Dirección. Nesto la pinta en
  el selector de clientes (y su selector de entrega), la cabecera del detalle del pedido y la lista de la ficha (`DireccionParaMostrar`).
- **Entrega por defecto de un cliente que ya existe (NestoAPI#879 corte 3)**: facturación = ficha con ClientePrincipal; entrega por defecto =
  `ContactoDefecto` de la principal (`ReglaContactoDefecto`, pura). `GET api/Clientes/ContactoDefecto?empresa=&cliente=` → { Empresa, Cliente,
  ContactoFacturacion, ContactoDefecto } (404 sin cliente). `PUT api/Clientes/ContactoDefecto` { Empresa, Cliente, Contacto } (`[Authorize]`, sin
  grupo, como PUT api/Clientes): contacto del cliente y activo (400 de usted si no; 404 sin cliente), escribe ContactoDefecto (+Usuario) en las
  principales ACTIVAS y una fila en Modificaciones (Tabla `ClienteContactoDefecto`) en una transacción; si ya lo era, 200 con `Cambiado` false y
  nada escrito. No cambia la principal. Nesto: pestaña «Direcciones» de la ficha (ClienteComercial). OJO: hoy Nesto NO propone ese contacto al
  elegir cliente para un pedido (el selector pide el principal); cambiarlo está pendiente de decisión.
- **Dirección 2 en todos los destinos (19/11/26, Carlos «adelante con todo»)**: etiquetas y envíos = `DireccionEnvioCliente` (paradas de notas
  de entrega y de cobro de la 14 —`ServicioParadasNotaEntrega.ConstruirEnvio`, `DestinoNotaEntrega.Direccion2`—, etiqueta de recogida sin parada);
  pantallas, correos y PDF = `DireccionCompletaCliente` (packing de Ariadna y en PDF —el SP no se toca: la API completa por empresa del pedido—,
  correo de pedido/presupuesto, dirección de ENTREGA de factura/albarán/pedido/nota/ticket vía `IServicioFacturas.LeerDireccion2Cliente`, inscritos,
  correo de clientes nuevos, facturación de tienda, aviso de cambio de contacto). Aditivos: `RecogidaSolicitadaDTO.Direccion2/DireccionCompleta`,
  `ComprobacionRecogidaImprevistaDTO.DireccionCompleta`, `AlquilerCabeceraDTO.Direccion2Cliente`, `ResumenPedidoVentaDTO.direccionCompleta` (+contacto).
  Listas: `DireccionCompletaCliente.RellenarPorEmpresa` (una lectura por empresa). Sin columnas o sin Dirección 2, lo de siempre.
- **Corte 2: al MODIFICAR (19/11/26)**: GET GetClienteCrear trae `TipoLugar` y sus campos descompuestos (`Direccion2Cliente.Descomponer`, la inversa
  de Componer; lo que no casa, «Otro» con el texto) + `Direccion2` (solo lectura). PUT: `Direccion2Cliente.ParaModificar` —con tipo, Componer (la
  regla del alta, se valida siempre); `QuitarDireccion2` = borrar; sin nada = NO TOCAR (NestoApp y Nesto viejos); se exige solo si la Dirección
  CAMBIA y `ExigirDireccion2Alta` = 1—. La nueva va a la sesión (`Direccion2EnSesion`) antes del SaveChanges (correo del disparador) y se escribe
  antes de publicar a Odoo (la modificación publica siempre). Nesto: «¿Qué es?» también al modificar (sin tipo elegido, ni aviso ni envío).
- **Fiscal (bloque 4a, Carlos 19/11/26)**: `DireccionCompletaCliente.ParaFiscal` = la completa si cabe ENTERA en 200 (CabFacturaVta.DireccionFiscal);
  si no, la Dirección sola. Solo en facturas que se emiten desde hoy (`ServicioFacturas` al guardar los datos fiscales); una factura emitida no
  cambia (la guardada se pinta tal cual; la antigua sin datos guardados, de la ficha SIN Dirección 2). También en pedido/albarán/proforma (fiscal
  de la ficha), PDF del 347 y mandato SEPA (DireccionDeudor). Verifactu no manda dirección (solo NIF y nombre). Rectificativas copian la de la original.
- **Tienda online y Aplázame (bloque 4b, 19/11/26)**: aditivos `DireccionCompleta` en GET api/Pedidos/Cliente/Direcciones
  (`DireccionEntregaClienteDTO`) y `direccionCompleta` en el ClienteDTO del login de la tienda (`direccion` no cambia en ninguno). Checkout de
  Aplázame (app #663 y pago a plazos #864): la calle (`street`) = `ConstructorCheckoutAplazame.Calle`, con la Dirección 2 si cabe ENTERA en 100
  (`LONGITUD_CALLE`, tope prudente: el código no tenía ninguno); si no, la Dirección sola.
- **Devueltos (seguridad, 19/11/26)**: GET Devueltos, Reenviar, Abonar y HaVuelto dan 403 a un token de cliente de la tienda
  (`ValidadorAccesoCliente.EsClienteTienda`). Empleados y vendedores, como antes.
- **Pedidos de la tienda online: `address2` y `company` (NestoAPI#878, parte del importador, Carlos 26/11/26)**: el importador de pedidos web está en
  NESTO (`CanalesExternos/CanalExternoPedidosPrestashop.TransformarPedido`), no en la API: hoy mete `address2` como una línea suelta en los comentarios
  y lo pega a la calle de la etiqueta (`pedidoExterno.Direccion = address1 + " " + address2`, que «Crear etiqueta» copia tal cual al envío pendiente);
  `company` no se usa. NADIE en la API lee la dirección de los comentarios. En la API: LA REGLA (pura) `Direccion2PedidoTienda`
  (`Infraestructure/Clientes/Direccion2PedidoTienda.cs`): `Componer(address2, company)` → {Direccion2 en mayúsculas, ≤ 100; con company no contenida
  en address2 (sin tildes ni mayúsculas) «… · RÓTULO: COMPANY» o, sin address2, «LOCAL · RÓTULO: COMPANY»; TipoLugar «Local» con company, si no null:
  nunca «Piso»}; `LineaComentario` («DIRECCIÓN 2: …») y `LeerDeComentarios` (tolerante: tildes, mayúsculas, espacios; la primera no vacía). GET
  ParaAgencia (`GestorPedidoParaAgencia.Montar`) trae aditivos `Direccion2Pedido` y `TipoLugarPedido` de esa línea, APARTE de la ficha (la del 31517 no
  es la dirección de entrega: no se mezcla con `ClienteFicha.Direccion`). «TOTAL PEDIDO:» no cambia.
  Falta en Nesto (CanalesExternos): (1) en los comentarios, sustituir la línea suelta de `address2` por la línea «DIRECCIÓN 2: …» con lo compuesto
  (gemela C# de `Componer`, o la misma regla copiada con sus pruebas); (2) `pedidoExterno.Direccion` = solo `address1` y la Dirección 2 al envío con la
  regla de `DireccionEnvioCliente.Componer` (Dirección + «, » + Dirección 2 si cabe en 80; si no, delante de las Observaciones); (3) opcional: que
  Agencias pinte `Direccion2Pedido` del DTO al abrir un pedido web.

### Cliente que cambia de forma fiscal: su número anterior y los «clientes equivalentes» (NestoAPI#876 cortes 1 y 2, 26/11/26)
- Origen `CAMBIO-FISCAL` (OneShot del 17/11, EsLead 0): cliente que ya lo era y factura con otra razón social o NIF; número nuevo. Fiscalmente otro
  cliente; comercialmente el mismo. NADA fiscal ni contable se hereda (facturas, extracto, deuda, remesas, 347, Verifactu, riesgo).
- Dato: `dbo.ClientesAnteriores` (Empresa, Cliente, ClienteAnterior, Fecha, Usuario; PK Empresa+Cliente, CHECK distinto; fuera del EDMX,
  `Scripts/Issue876_ClientesAnteriores.sql`). Sin la tabla: el alta no lo pide ni guarda nada, la ficha da null (como antes).
- Alta (`POST api/Clientes`, `ClientesController.ClienteAnterior.cs`): `ClienteCrear.ClienteAnterior`. LA REGLA (pura) `ReglaClienteAnterior`
  (`Infraestructure/Clientes/ClientesAnteriores.cs`): con CAMBIO-FISCAL es obligatorio (400 «Con el origen «Cambio de forma fiscal» indique el número de
  cliente que tenía antes…»), tiene que existir en la empresa (400 «No existe el cliente 99999 en la empresa 1…») y no puede ser él mismo (alta de un
  contacto: 400); sin ese origen, 400. Mismo NIF: solo aviso. Se guarda DESPUÉS del alta (como el origen; si falla, ELMAH y el cliente queda), con
  fila en Modificaciones (Tabla `ClientesAnteriores`). Respuesta: `Cliente.ClienteAnterior` y `AvisoClienteAnterior` (NotMapped, solo si hay).
  Ficha: `ClienteDTO.ClienteAnterior` en GET api/Clientes (un cliente) y `ClienteCrear.ClienteAnterior` en GetClienteCrear. El PUT no lo toca.
- Pieza única (corte 2) `IClientesEquivalentes.De(empresa, cliente)` → el cliente y sus anteriores en cadena, del más reciente al más antiguo, sin
  relleno (`ClientesEquivalentes`: todos los enlaces de la empresa en una lectura, caché de 5 min, `Invalidar()` al guardar; sin tabla o si falla,
  solo el cliente, ELMAH una vez; LA REGLA `ReglaClientesEquivalentes.Cadena`: sin ciclos, tope 20 saltos). SIN CONECTAR todavía: plantilla de venta y
  rapports (corte 3) y sugerencias de llamadas, fechas de compras y embudo (corte 4) la usarán; nunca lo fiscal.

### Cliente que cambia de forma fiscal: copiar sus condiciones y pasar sus pedidos (NestoAPI#876, corte nuevo, Carlos 26/11/26)
- Va ENCIMA de los cortes 1 y 2 (sección anterior): la misma `dbo.ClientesAnteriores` (una fila por cliente nuevo; el script, que ya enlaza
  42178 ← 37283, también pasa a la forma definitiva una tabla creada con la forma provisional de la mañana del 26/11) y un solo acceso a ella,
  `RepositorioClientesAnterioresSql` (la copia usa su SQL dentro de su transacción; `ServicioCambioFiscal` mira la tabla y guarda el enlace de
  PasarPedidos por él). Las consultas comerciales siguen sin heredar nada.
- Carlos: «al crear un cliente porque otro ha cambiado los datos, se copian todos los datos; así, aunque se recalcule un pedido al pasarlo al cliente nuevo,
  no cambia nada… Si algún dato el usuario pide no copiarlo, al recalcular sí que va a cambiar». LA REGLA (pura) `ReglaCopiaCambioFiscal.Planificar`
  (`Infraestructure/Clientes/CambioFiscal/CopiaCambioFiscal.cs`), por bloques (`BloquesCambioFiscal`): `Comercial` (estado —no si el anterior está de baja—,
  vendedor y VendedoresClienteGrupoProducto), `Fiscalidad` (SOLO Clientes.IVA; uno sin IVA no se copia: iría a la empresa espejo), `CondicionesPago`
  (CondPagoClientes: se quitan las del nuevo que el anterior no tiene y se ponen las suyas; aviso si la forma necesita cuenta y el nuevo no tiene CCC: el
  IBAN y el mandato NUNCA se copian), `Descuentos` (filas de DescuentosProducto del cliente, sin las caducadas; misma clave —producto, familia, grupo,
  subgrupo, filtro, proveedor, contacto, cantidad mínima— ya en el nuevo: no se duplica y, si vale distinto, no se toca y se avisa), `Ofertas`
  (OfertasPermitidas propias, sin caducadas ni repetidas) y `Ficha` (comentarios solo si el nuevo no tiene, días de servir, ruta, periodo de
  facturación, NoComisiona, MantenerJunto). Cada ficha viva del nuevo copia de la del MISMO contacto en el anterior o, si no la hay, de su principal;
  descuentos y ofertas con contacto NULL siguen NULL y los de un contacto que el nuevo no tiene, no. Idempotente (una segunda pasada no hace nada).
- Enlace (`ReglaCopiaCambioFiscal.DecidirEnlace`): vale si consta en ClientesAnteriores; si no consta nada y el nuevo tiene origen CAMBIO-FISCAL
  (ClientesOrigen), se crea al copiar o pasar pedidos; si no, o si consta con OTRO anterior, 409 `{Message, Codigo: "NO_ES_CAMBIO_FISCAL"}`. Permisos
  (`MotivoSinPermiso`): Administración, Dirección, Informática y el vendedor del anterior o su jefe de equipo (`VendedoresEquipoString`); cliente de la tienda 403.
- `GET api/Clientes/CambioFiscal/Previa?empresa=&clienteAnterior=&clienteNuevo=` (solo lectura) → `CopiaCambioFiscalDTO` {Enlazados, SeEnlazaAlCopiar,
  MotivoNoSePuedeCopiar, Bloques [{Bloque, Nombre, Anterior[], Nuevo[], Cambios[], Avisos[], HayCambios}], PedidosPendientes [{Pedido, Contacto, Fecha,
  Importe (base de lo pendiente), Total, Lineas, TienePicking, Pickings, NotaEntrega, SePuedePasar, Motivo}], Mensaje}. Pendiente = líneas -1..1 con
  cantidad y sin albarán; «se puede» con la regla de CambiarCliente con el picking admitido (envío de agencia, efectos o tarjeta lo paran).
- `POST api/Clientes/CambioFiscal/Copiar?ensayo=true` {Empresa, ClienteAnterior, ClienteNuevo, Bloques? (null = todos)}: ensayo por defecto. De verdad,
  en UNA transacción ADO (`RepositorioCambioFiscalSql`, applock `ClienteCambioFiscal:{empresa}:{nuevo}`, datos releídos dentro; otro a la vez → 409
  `OCUPADO`): enlace si falta, el plan y UNA fila en Modificaciones (Tabla `ClienteCambioFiscal`, Anterior «empresa|anterior → nuevo», Nuevo = JSON
  con bloques y cambios). Sin nada que copiar ni enlace que crear, no escribe nada. Si cambia la ficha, publica cada contacto a Odoo
  (`PublicarClienteSincronizar`, como el PUT); si Odoo falla, queda copiado y lo dice en Avisos. Sin la tabla, 503.
- Alta (`POST api/Clientes`): `ClienteCrear.ClienteAnterior` con las validaciones de los cortes 1-2 (`ReglaClienteAnterior`: con CAMBIO-FISCAL,
  obligatorio; existe; no es él mismo; sin ese origen, 400; mismo NIF, solo aviso) y, además, `CopiarDelAnterior` (lista de bloques; vacía = todos;
  null = no copiar, como hoy; sin ClienteAnterior o con un bloque que no existe, 400). Tras crear: el enlace (`GuardarClienteAnteriorTrasAlta`) y, si
  se pide, el MISMO Copiar (`TrasAltaCambioFiscal`). Nunca rompe el alta: la respuesta (Cliente) trae `ClienteAnterior`, `AvisoClienteAnterior`,
  `CopiadoDelAnterior` (lo copiado) y `AvisoCopiaClienteAnterior` (por qué no se copió, o los avisos de la copia). OJO: la ficha que devuelve el alta es
  la de antes de copiar (relean el cliente).
- `POST api/Clientes/CambioFiscal/PasarPedidos?ensayo=true` {Empresa, ClienteAnterior, ClienteNuevo, Pedidos[], CreadoSinPasarValidacion?}: mismo enlace y
  permisos; pedido a pedido por el `CambiarCliente` de SIEMPRE (`PedidosVentaController.CambiarClienteDelPedido`, un controller y un contexto por pedido),
  al mismo contacto si el nuevo lo tiene o a su principal → {Ensayo, Pedidos [{Pedido, Pasado, TeniaPicking, SinCambiosDeImportes, Codigo
  (NO_EXISTE, NO_ES_DEL_CLIENTE_ANTERIOR, CAMBIO_CLIENTE_CON_PICKING_CAMBIA, NO_SE_PUEDE, ERROR), Mensaje, Cambios, Avisos}], Pasados, Rechazados}.
  Con picking, la regla nueva de `docs/claude/pedidos-venta.md` (solo si no cambia nada).
- Nesto (pendiente): en el asistente del alta con origen «Cambio de forma fiscal», pedir el cliente anterior, llamar a la Previa y enseñar una casilla
  por bloque (marcadas) con Anterior/Nuevo/Cambios/Avisos → `CopiarDelAnterior`; después, la lista de pedidos pendientes con casillas (ensayo primero
  para enseñar qué cambiaría; los de picking que cambiarían, sin casilla y con el porqué) → PasarPedidos. En la ficha, botón «Copiar del cliente
  anterior» (Previa + Copiar con los bloques marcados). NestoApp: lo mismo en su alta de cliente (issue gemela); sin pantalla nueva en la ficha.

### Origen del cliente: campaña (lead) o canal (NestoAPI#848, 09/11/26)
- `dbo.OrigenesCliente.EsLead` (DEFAULT 1): 1 = campaña (META-HARMONIA-0926…: hay que llamarlo), 0 = canal (WEB, AMAZON, TIENDA-REINA,
  TIENDA-ALCOBENDAS, CURSOS: solo un dato para medir). `Scripts/Issue848_OrigenesCanal.sql` ANTES de la API (la API lee la columna).
- Los de canal NO son leads: `SQL_LEADS` filtra `o.EsLead = 1` y `ReglasLeadsCampana.PendienteDeLlamar` los descarta (job
  `leads-sin-llamar`, sugerencias de contacto); `POST api/Clientes/Origenes/ImportarLeads` con uno de canal → 400. A Odoo
  (`nesto_sync`) el origen solo viaja desde el importador de leads, así que los de canal no llegan.
- Propuesto por usuario (dato, no código): ParametrosUsuario «OrigenClientePorDefecto» (empresa 1; fila del usuario o «(defecto)»,
  vacía = sin propuesta; no se crea fila al leer). `GET api/Clientes/Origenes` marca `PropuestoParaElUsuario`; Nesto lo preselecciona en el alta.

### Embudo de Odoo: bloque diario (11/11/26)
- Job `embudo-bloque-diario` TODOS los días (también fines de semana y festivos, a propósito) a las 7:00 de Madrid
  (`EmbudoBloqueDiarioJobsService`): el siguiente bloque NV-AAAA-MM-NN (NN vuelve a 01 cada mes) de 50 por `ServicioEmbudoClientes.Republicar`
  (el mismo camino que `POST api/Clientes/Sync/Republicar`: ensayo y, si llena el bloque, de verdad). Si hoy ya hay un bloque, no lanza.
  Sin tabla o si falla la consulta o el lanzamiento → no lanza, ELMAH y campana a Dirección. Menos de 50 pero alguno → lanza con
  los que haya, sin aviso (agotada la cola entran unos 45 al mes); ninguno → no lanza, solo ELMAH. Interruptor: ParametrosUsuario «(defecto)»
  `EmbudoBloqueDiario` = 1 (sin la fila, apagado) y tamaño `EmbudoTamanoBloque` (sin fila o no válido, 50; tope 100 por Office 365, se recorta y se
  registra); los crea `Scripts/EmbudoBloqueDiario_Interruptor.sql` (encendido, 50). Se ven y cambian con GET/PUT
  `api/Clientes/Embudo/Configuracion` (Dirección/Informática; Usuario2 + fila en Modificaciones). El lanzamiento a mano sigue topado en 50. Orden (manual y job, el mismo `SqlCandidatos`, 11/11/26): los más
  recientes primero (último pedido DESC; luego los que nunca compraron por FechaAlta DESC), los antiguos al final (rebotan más).
  **Solo dormidos (Carlos 17/11/26)**: entran únicamente los que nunca han comprado o con último pedido de hace más de
  `RepositorioClientesEmbudoSql.ANIOS_SIN_COMPRAR` (2) años (`@dormidoDesde`), también los de origen AMAZON/WEB. Sin ese filtro los bloques
  NV-2026-11-03..08 metieron 325 clientes vivos; Carlos los saca de `ClientesEmbudo` con `Scripts/OneShot_20261117_Embudo_SacarClientesVivos.sql`.
- **Numeración (18/11/26)**: un número ya usado no se repite aunque el bloque se borre de `ClientesEmbudo` (Odoo agrupa por el nombre, también lo archivado): el job apunta el último
  lanzado en ParametrosUsuario «(defecto)» `EmbudoUltimoBloque` (sin rastro en Modificaciones; sin fila, solo el registro) y `SiguienteBloque` nunca numera por debajo.
  `BLOQUES_QUEMADOS` = NV-2026-11-08 (el 17/11 se borraron 03-08 y el del 18/11 repitió el 03; desde el 19/11 sigue en el 09).
- Job `embudo-ajuste-ritmo` 6:45 Madrid (**APAGADO desde el 17/11/26**: solo actúa con ParametrosUsuario «(defecto)» `EmbudoAjusteRitmo` = «1»; sin fila, nada. Se apagó al pasar a solo dormidos: la recomendación de Odoo se calculó con clientes vivos; Odoo tampoco calcula. Encender con ~5 tandas de dormidos) (Odoo calcula a las 4:30 UTC; 404 = sin cálculo aún: nada, sin aviso) (`EmbudoAjusteRitmoJobsService`): GET {OdooEmbudoRitmoUrl}/nv/embudo/recomendacion con cabecera
  `X-NV-Clave` = OdooEmbudoRitmoClave (las dos en secretos.config; sin ellas, nada ni aviso). Reglas puras `ReglasRitmoEmbudo`: escalones
  50 → 75 → 100 (tope 100); «subir» un escalón si hace 7 días del último cambio (`EmbudoTamanoBloqueFecha`, también el PUT) y el dato
  (calculadoEl) tiene menos de 8, sin pasar de escalonRecomendado; «bajar» un escalón en el acto; «parar» apaga el interruptor;
  dato viejo/ilegible/403/red → nada y aviso a Dirección una vez al día (`EmbudoRitmoUltimoAvisoFallo`). Cada cambio: Modificaciones
  con motivo y cifras de Odoo + campana. La última recomendación (`EmbudoRecomendacion*`) sale en GET Configuracion. «subir» exige además el DKIM de Office 365
  publicado (`IComprobadorDkim`, DNS de selector1._domainkey.nuevavision.es; hoy NO existe): si no, no sube y avisa como mucho 1 vez/semana.
- **Orígenes AMAZON y WEB (Carlos 16/11/26)**: además de los del vendedor NV, `SqlCandidatos` añade los clientes con origen de
  `ServicioEmbudoClientes.ORIGENES_QUE_ENTRAN` (AMAZON, WEB; constante en código, EsLead = 0) sea cual sea su vendedor y su estado (algún contacto
  ≥ 0), SIEMPRE con alguna persona con correo y boletín (`EntraPorOrigen`; también con SoloConCorreo = false) y sin estar en `ClientesEmbudo`.
  Mismo orden y tope. Por SQL dinámico (sin ClientesOrigen o sin EsLead, como antes). La respuesta trae `Origenes` (cliente → origen) e `IncluidosPorOrigen`.

### Sugerencias de llamadas y ofertas vigentes (NestoAPI#866, 16/11/26)
- Siempre hay ofertas (bimestrales, de marca, campañas; Carlos 16/11/26): impulso SIEMPRE encendido salvo ParametrosUsuario empresa 1
  «(defecto)» `Rapports.ImpulsoOfertas` = 0/false/no/off/apagado (sin fila = encendido). Regla pura `ReglasImpulsoOfertasContacto`, después del
  modelo como el del cheque. Oferta = fila global de DescuentosProducto desde 1 unidad que «parece campaña» (fechas, audiencia o Campana, como
  `api/Campanas?soloCampanas`) o N+M de OfertasPermitidas con fechas, vigente con la regla única `Vigencia`, o SIN fechas y con `FechaModificación`
  de los últimos 90 días (`ReglasImpulsoOfertasContacto.DIAS_NM_SIN_FECHAS`, `CuentaOfertaNm`, 18/11/26: las bimestrales suelen cargarse sin fechas;
  su fecha de entrada, `FechaEntrada`, es esa FechaModificación y no tienen fin); de cada producto, la de más descuento
  (precio especial = 1 − precio/PVP; N+M = M/(N+M)). Caché 10 min; compras de la cartera en UNA consulta por vendedor (`ImpulsoOfertasContactoSql`,
  misma asignación que la cartera). Producto habitual = ≥ 2 días con compra en 12 meses; ciclo = mediana entre compras; puntuación: le toca > descuento > peso.
- Suben como mucho 5 al día detrás del cheque (Origen «Ofertas», prefijo «Compra el »), solo si su mejor producto ya le toca y no se habló con él
  desde que entró la oferta (en 14 días), ni intento < 2 días ni pedido < 6. Motivo: «Compra el 45929 (NOMBRE) cada 45 días y lleva 64 sin comprarlo; hoy
  tiene un 20 % de descuento (hasta el 30/11). No hablamos con él desde el 02/10.» («solo hasta» si caduca en ≤ 7 días, «oferta nueva» si entró hace ≤ 7).
- Motivo explicado (corte 2): las de Origen Probabilidad llevan detrás «Sobre todo porque …» con las 2 causas de más contribución positiva del LightGBM
  (`ExplicacionModeloContacto`, CalculateFeatureContribution de ML.NET 4.0.2; one-hot sumado por rasgo, plantilla por rasgo). Solo modelo general; si falla, sin ella.
- Datos (corte 3, `Scripts/Issue866_SugerenciasContactoOfertas.sql`, columnas opcionales fuera del EDMX): Origen, ProductosOferta (JSON de los 3 mejores en
  TODAS las sugerencias con alguno: grupo de control), y job `sugerencias-contacto-resultado` 3:25 Madrid: PedidoEn7Dias, PedidoConOferta, ImportePedido7Dias
  de las de hace 8-60 días (un UPDATE). Para decidir si se añaden rasgos de ofertas al modelo.

### App de clientes (TNV): lector y refresco del token (registros de IIS 25/11/26)
- Lector: `GetProducto(codigoBarras)` (solo ese; `BuscarPorCodigoBarras` y los flujos de almacén siguen preguntando siempre) descarta las
  fichas anuladas (Estado < 0 o null) si queda alguna viva (`ProductosController.CoincidenciasAConsiderarLector`, pura): una viva → esa, sin 409;
  varias vivas → 409 solo con ellas; todas anuladas → 409 con todas (8436538394380: 44967 anulado / 45931 vivo).
- Refresco: `api/auth/refreshToken` ya no corta a un mes de caducado el token: la ventana desde la caducidad son los mismos días que la
  absoluta desde el login (`Auth:DiasMaximosSesionTienda`, 90; `PoliticaRefrescoToken.VentanaDesdeCaducidadVencida`). 15 de 26 clientes
  echados tras un mes sin abrir la app no volvieron a entrar. Firma, absoluta y revalidación del cliente, igual; `refreshOAuthToken` no cambia.

### Ficha comercial: lo que la auditoría Nesto#644 dejó para la API (26/11/26)
- `PUT api/Clientes/ClienteComercial` (solo lo llama Nesto, ventana ClienteComercial; NestoApp usa `PUT api/Clientes`, no este). (15) Ya no hace
  `db.Entry(clienteDB).State = Modified` (escribía TODAS las columnas con lo leído): la entidad está seguida y EF solo escribe lo cambiado. El usuario
  de auditoría (ficha, fila de peluquería y motivo #716) = `UsuarioAuditoriaHelper.Resolver(User, cliente.usuario)` (el del DTO solo sin token), vía
  `AplicarCambiosClienteComercial(clienteDB, dto, usuarioAuditoria)` (con `ParaAuditoria`: nunca vacío).
- (7) Permisos (`ValidadorCambioClienteComercial`): `DatosPermisoClienteComercial.CambiaPais` (`CambiaElPais`: no vacío y distinto, sin relleno ni
  mayúsculas), `CambiaEstadoGrupo` (`CambiaElEstadoDelGrupo`: fila con estado distinto o la fila no existe) y `CreaFilaGrupo` (sin fila y llega vendedor:
  el destino también se comprueba contra el equipo) entran en la condición de `Evaluar`. Antes un vendedor raso cambiaba el país o el estado de
  peluquería de un cliente de fuera.
- (6) `VendedorGrupoProductoDTO.estado` pasa a `short?` (no viaja en el JSON si es null: los vendedores del pedido no lo tienen y Nesto lo lee como
  Short). `GestorComisiones.AplicarVendedorGrupo`: vendedor null = no tocar el vendedor; estado null = no tocar el estado. Al CREAR la fila, el estado que
  viene (sin él, VISITA_PRESENCIAL): el segundo PUT de Nesto (`PayloadEstadoPeluqueria`) ya sobra (inofensivo).
- (5) Copia vieja: `ClienteDTO.original` {estado, vendedor, pais, vendedorPeluqueria, estadoPeluqueria} (opcional, solo de entrada; `OriginalClienteComercialDTO`).
  LA REGLA (pura) `ReglaClienteComercialCambiado.Evaluar` (`Infraestructure/Clientes/ClienteComercialCambiado.cs`): por cada campo que el PUT CAMBIA (mismo
  criterio que al aplicarlo), si en la BD ya no vale lo de `original` → 409 `{ Message, Codigo: "CLIENTE_CAMBIADO", details: { campos, usuario, fecha } }`
  («La ficha del cliente 40445 la ha cambiado Aida a las 15:14 desde que usted la abrió (estado). Recárguela y vuelva a hacer los cambios.»; «el dd/MM a las…»
  si no es de hoy), después del 403 y antes de escribir nada. Lo que el PUT no cambia no cuenta. Textos null = vacío; estados de `original` a null no se
  comparan; sin fila de peluquería coincide con un original sin vendedor de peluquería. Quién y cuándo: `Clientes.Usuario`/`Fecha_Modificación` o los de la
  fila de peluquería, lo más reciente de lo que choca. Sin `original`, como antes. Nesto: mandar `original` con lo cargado (`ValoresComercialesFicha`) y tratar el 409.

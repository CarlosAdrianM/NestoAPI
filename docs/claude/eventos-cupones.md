# Eventos, inscripciones y cupones

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Inscripciones, señal, asistencia, paso a gratuito, cupones de evento.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Eventos: candidatos a invitar, en MODO SOMBRA (NestoAPI#850, 11/11/26)
- Solo se recogen datos (nada visible; cortes 2-4 en la issue). `dbo.EventosCandidatos` (fuera del EDMX, `Scripts/EventosCandidatosSombra.sql`):
  foto por evento + cliente NV + día con características y puntuación `ReglaCandidatosEventos` (`reglas-1`, umbral 0,30, máx. 500) más
  300 de control al azar (semilla por evento y día). El resultado NO se copia: `dbo.vEventosCandidatosResultado`. Distancia: dirección de
  `DireccionesCoordenadas` o centro del CP (`dbo.CentrosCodigosPostales`, `Scripts/CentrosCodigosPostales_GeoNames.ps1`, GeoNames CC-BY)
  a `SedesReparto`. Al crear/anunciar (Hangfire) y job `eventos-candidatos-sombra` 3:15. `GET api/Eventos/CandidatosSombra` (Dirección/Informática).

### Teléfono de la ficha en los inscritos (fleco de la sugerencia 1362, 19/11/26)
- `InscripcionEventoDTO.Telefono` (solo lectura): `Clientes.Teléfono` del cliente/contacto de la inscripción, de la misma consulta por tanda que
  la dirección (`Completar`). La hoja «Imprimir lista» de Nesto usa el de la persona que asiste y, si no, este.

### Evento de pago que pasa a gratuito (Novedades 1233, 12/11/26)
- `PUT api/Eventos/{id}` que deja las dos señales a 0: las Reservadas sin dinero ni documentos pasan a Inscrita / Gratuita (FechaLimitePago
  y FormaPago a NULL, rastro en Modificaciones) y sus enlaces de pago pendientes (cadena de reintentos incluida) se anulan con el UPDATE de
  `AnulacionEnlacesPago`, todo en la transacción del evento (si un enlace ya no está pendiente: 409 y nada cambia). Las Caducadas igual solo si
  caben TODAS en las plazas libres (no las «No podré ir» ni las de un cliente con otra viva). Reglas puras: `ReglasPasoAGratuitoEvento`;
  transición exclusiva `ReglasInscripcionEvento.PuedePasarPorPasoAGratuito` (no está en `PuedePasar`).
- Con alguna pagada, facturada, con lista pasada, cobro, señal, factura del curso, cupón o enlace cobrado/compartido con otro evento: 400 como
  antes (todo o nada). Nunca se deshacen facturas ni cupones. La respuesta trae `InscripcionesConvertidasAGratuitas`, `InscripcionesRecuperadas`,
  `CaducadasSinRecuperar`, `EnlacesPagoAnulados` y `MensajePasoAGratuito`.
- Un cobro tardío del enlace de una Inscrita no la paga ni factura: ELMAH + aviso de pago fuera de plazo a Administración. Los jobs de aviso,
  liberación y caducidad se saltan los eventos que hoy son gratuitos. `CrearEvento` con productos requeridos y sin importes nace 0/0.

### Lista de asistencia: eventos online y lista sin nadie marcado (NestoAPI#908, Masterclass Rosácea 24/11/26)
- LA REGLA (pura) `ReglasListaAsistencia` (`Infraestructure/Eventos/ListaAsistenciaEvento.cs`). Evento ONLINE (`FormatosEvento.EsOnline`; el Mixto
  sí pasa lista): `POST api/Eventos/{id}/Asistencia` → 400 `LISTA_EVENTO_ONLINE` sin tocar nada; al día siguiente el job de siempre
  (`DarPorAsistidosSinLista`) da por asistidas a las pagadas (las «No podré ir», No asistió, como siempre) y su correo a la tienda dice «Evento
  online: no se pasa lista» (no «nadie ha pasado lista… pasen lista»). `EventoDTO.SePasaLista` y `TextoListaAsistencia` (solo lectura, «Evento
  online: todas las inscritas pagadas se dan por asistidas.»). Cualquier evento: guardar la lista con inscritas pagadas y NINGUNA marcada → 409
  `LISTA_SIN_ASISTENTES` (details `perderanElCupon`) salvo `AsistenciaEventoDTO.Confirmar` = true. «Muy pocas respecto a las inscritas»: sin umbral
  decidido, no se pregunta. Gemela en Nesto.

### La señal de un evento se cobra en Cajas (caso del cliente 33895, Carlos 19/11/26)
- `GET api/Eventos/Inscripciones/SinPagar?cliente=&empresa=1`: reservas (Reservada/Caducada, de pago, evento de hoy en adelante) con `Importe`
  (la señal), `Documento` INS{id}, `Cobrable`/`Motivo` (`ReglasCobroSenalCaja.MotivoNoCobrable`: señal de #591, propuesta de #615 sin confirmar,
  enlace cobrado, pedido de cursos #691, o `IConfirmadorSenalEvento.MotivoNoFacturaSolo`: interruptor apagado / no automatizable) y `CobroYaApuntado`.
- `POST api/Eventos/Inscripciones/{id}/CobroCaja` {FormaPago EFC|TAR|TRN, Importe, CuentaCobro?, Fecha?, Diario?, Delegacion?, FormaVenta?,
  ConceptoAdicional?} (`ServicioInscripcionesEventos.CobrarSenalEnCaja`, `Infraestructure/Eventos/CobroSenalCaja.cs`): importe ≠ señal → 400 (entera o
  nada); tiendas efectivo/tarjeta, Administración/Dirección también transferencia (`PermisosEventos.PuedeCobrarSenalEnCaja`). En UNA transacción
  (`RepositorioCobroSenalCaja.Registrar`, applock por inscripción, estado releído): cobro a cuenta «INS{id}» = los apuntes de un cobro a cuenta de Cajas
  (`ConstructorApuntesCobroSenalCaja`, prdContabilizar por el núcleo de siempre), anula el enlace pendiente y deja la inscripción Pagada; si ya hay cobro
  INS{id} no lo repite. Después, `TrasPagar` (el corte 2 de siempre): pedido + factura exenta, `LiquidadorSenalCurso` liquida el cobro INS{id} contra el
  efecto (LIQUIDADA) y cupón. Relanzar = terminar sin cobrar otra vez. Rastro en Modificaciones (Tabla `CobroSenalCaja`).
- «Marcar pagada» (y inscribir ya pagada): solo Administración/Dirección/Informática (`PuedeMarcarPagadaAMano`, 403 de usted). Facturar a mano y la
  asistencia siguen con `PuedeMarcarPagada`. POST/PUT PedidosVenta con la referencia del curso de un evento vivo (sin EventoId; en el PUT, líneas nuevas):
  aviso `CursoVendidoAMano` (POST en `AvisosGrabacion`), se guarda igual. Nesto: Cajas › Cobros enseña las reservas; Marcar pagada oculto a las tiendas.

### Datos de la transferencia de una inscripción (NestoAPI#887, duda de Paloma, novedad 1421; Carlos 23/11/26)
- `GET api/Eventos/Inscripciones/{id}/DatosTransferencia` (solo lectura; la casa: cliente de la tienda 403; 404 sin inscripción; 409 si no es una reserva
  —Reservada— de pago; 400 si la empresa no tiene cuenta) → {InscripcionId, EventoId, Evento, Empresa, Cliente, Contacto, Iban, Titular, Concepto «Cliente
  29606 - INS123» (el documento del cobro en Cajas), Importe (la señal según la modalidad), Texto (una línea por dato, para pegar), Recordatorio («Cuando
  llegue la transferencia, Administración apunta el cobro en Cajas › Cobros (documento INS123).»: para quien copia, no va en el Texto)}. Como el del pedido
  de prepago (sugerencia 396): IBAN y beneficiario de `LectorDatosPagoEmpresa`. LAS REGLAS (puras) `ReglasDatosTransferenciaInscripcion`
  (`Infraestructure/Eventos/DatosTransferenciaInscripcion.cs`).
- Los avisos de plazo de pago (`RecordatorioEventosJobsService.CorreoPago`) y de evento lleno (`PlazasLlenasEventosJobsService.CorreoAviso`) de una reserva
  por transferencia sin enlace dicen ahora beneficiario, IBAN, importe y concepto (`ReglasInscripcionEvento.ComoPagarSinEnlace(forma, datos)`) en vez de
  «si no tiene los datos de la cuenta, pídanoslos»; sin cuenta o si falla la lectura, lo de siempre (nunca rompe el aviso). Gemela Nesto: botón
  «Copiar datos de transferencia» junto a «Copiar enlace» en los inscritos.

### Cupón de evento adelantado por excepción (NestoAPI#885, 23/11/26)
- `POST api/Cupones/{codigo}/Adelantar` {Motivo 1..500} → 200 `CuponDTO`: un cupón de Origen «Evento» que hoy está Emitido (vale desde el día
  del evento) pasa a valer desde HOY (`ValidoDesde` = hoy y Estado Activo; ni importe, ni caducidad, ni inscripción ni factura). 400 sin motivo,
  404, 409 `CUPON_NO_SE_PUEDE_ADELANTAR` si ya vale, está usado, anulado, caducado o no es de evento (`ReglaAdelantarCupon.MotivoNoAdelantable`).
  UN lote con transacción (`RepositorioCupones.SQL_ADELANTAR`, UPDATE condicionado): el cupón y la fila de Modificaciones (Tabla `CuponAdelantado`,
  JSON con código, empresa, cliente, contacto, importe, ValidoDesde anterior y nuevo, usuario y motivo). Después, campana de Nesto a Administración
  (`AvisadorCuponAdelantado`, tipo `CuponAdelantado`; nunca rompe). Sin cambios de esquema.
- LA REGLA de permisos (pura, `ReglaAdelantarCupon` en `Infraestructure/Cupones/AdelantarCupon.cs`): Dirección siempre (sin leer nada); si no,
  la fila PROPIA de ParametrosUsuario empresa 1 `Cupones.PuedeAdelantar` (sin «(defecto)», no se crea al leer): «1» = cualquier cliente,
  «Equipo» = solo si `Clientes.Vendedor` del cliente/contacto del cupón (sin contacto, el principal) es él o de su equipo (`VendedoresEquipoString`;
  su vendedor = claim «Vendedor» o parámetro «Vendedor»). Sin permiso, 403; cliente de la tienda, 403.
- `CuponDTO.SePuedeAdelantar` en GET por código y en los del cliente: solo se miran permisos si hay algún Emitido de evento (una comprobación por
  petición; con «Equipo», una lectura de vendedores por empresa). X-API-KEY o cliente: false.
- Espejo de PrestaShop (#614): si el cart_rule está Activo allí, el adelanto pone `EspejoPendienteDesde` y `HayQueSincronizar`/`SQL_PENDIENTES`
  vuelven a mandar entero (PUT con el date_from nuevo) un Activo marcado pendiente; se intenta en el acto y, si falla, el job de cada 15 min.
- **Quién lo adelantó y aviso si no asiste (NestoAPI#910, para NestoApp#297, 26/11/26; sin script)**: `CuponDTO.Adelantado` (bool),
  `AdelantadoPor` (usuario sin dominio) y `AdelantadoEl` (fecha y hora) en GET por código y en los del cliente: del rastro «CuponAdelantado» de
  Modificaciones (no hay columnas), UNA lectura por petición (`LeerRastrosAdelanto`: Tabla + Fecha ≥ la emisión más antigua de los cupones de evento
  de la respuesta) y solo si hay alguno de evento; LA REGLA `ReglaAdelantarCupon.Adelantos` (el último por código; usuario del JSON). Si falla,
  ELMAH y sin esos datos. «No asiste» = la inscripción pasa a «No asistió» (la lista guardada sin marcarla, o el job del día siguiente con las «No
  podré ir»), es decir, `ServicioCupones.AplicarAsistencia` con false: si su cupón estaba adelantado, push de NestoApp `CuponAdelantadoSinAsistir`
  (`AvisadorCuponAdelantadoSinAsistir`: al vendedor del cliente —Clientes.Vendedor del contacto o el principal— y a su jefe de equipo,
  `JefeEquipo`; Datos tipo, empresa, cliente, contacto, codigo, importe, evento; texto de usted con el evento, el importe, quién lo adelantó y
  si se anuló o ya está usado en el pedido N). UNA vez por cupón: fila «CuponAdelantadoSinAsistir» (Anterior = el código) en Modificaciones
  (`AnotarAvisoSinAsistir`, INSERT … WHERE NOT EXISTS); volver a pasar la lista no repite. El cupón no se toca más de lo de siempre (anulado si no
  estaba usado; usado, ELMAH como antes). Nunca rompe la lista.

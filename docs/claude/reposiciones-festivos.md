# Reposiciones de tienda y calendario

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Diferencias de reposición, excepciones del calendario, festivos y días sin reparto.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Reposición del exceso de una tienda (sugerencia 1429, Manuel, 26/11/26)
- Una tienda (REI, ALC) devuelve a Algete lo que le sobra de una familia sin ir producto a producto (caso: esmaltes de Faby en Alcobendas).
  `POST api/Reposiciones/Exceso?ensayo=true` (ensayo por defecto: solo calcula) {Empresa, Origen REI|ALC, Destino? (ALG; distinto del origen),
  Familia (código o nombre, sin mayúsculas ni relleno), Grupo, Subgrupo, Productos[] (≤ 1000)}; sin ningún filtro → 400 (nunca «todo el almacén»).
  → {Ensayo, Origen, Destino, Lineas [{Producto, Nombre, Familia, NombreFamilia, Stock, EnOtraReposicion, Pendiente, StockMaximo, SinMaximo, Exceso}],
  NumeroLineas, TotalUnidades, LineasSinMaximo, Reposicion, Mensaje «Se mandarían 418 unidades de 183 referencias de Alcobendas a Algete. De ellas, N sin
  máximo puesto en la tienda, que se devuelven enteras.»}.
- LA REGLA (pura) `ReglaExcesoReposicion` (`Infraestructure/Reposiciones/ExcesoReposicion.cs`): exceso = min(Stock − StockMaximo, Stock − Pendiente), solo
  si > 0. Máximo = el «Stock Máximo (actual)» de la ficha (`ControlesStock.StockMáximo` de la empresa y el ORIGEN; no el sugerido); SIN fila = máximo 0
  (Carlos 26/11/26): entra con todo lo disponible y `SinMaximo` = true. Ficticios fuera; lo anulado (Estado < 0) NO se excluye (es lo que conviene
  devolver). Orden familia y nombre; más de 500 líneas → 400 de usted con cuántas salen y «acote el filtro».
- Lectura: UNA consulta NOLOCK (`LectorExcesoReposicionSql`) con el MISMO criterio que prdRellenarReposicionStock2 (su bloque «forzar envío de excedentes de
  una familia», `Scripts/Issue577_PropuestaReposicionSinBloqueo.sql`): Stock = ExtractoProducto del origen (empresa, su espejo y la 4) − lo comprometido
  para salir sin contabilizar (en preparación en el DiarioSalidaRep del origen o salida numerada; sale aparte en `EnOtraReposicion`); Pendiente =
  LinPedidoVta -1..1 del origen (empresa y espejo, sin el pedido 724488). Solo productos con Stock > 0.
- Permisos (también en el ensayo): cliente de la tienda online 403; `PermisoRellenarReposicionManual` («UsuariosRellenarReposicionManual») y
  `IServicioPreparacionReposicion.PuedeEscribir` (la gente de la tienda por AlmacénPedidoVta, Almacén o Dirección), los de POST api/Reposiciones.
  De verdad: `IServicioPreparacionReposicion.Crear` con las líneas calculadas (el mismo camino; sus 409 tal cual: otra en preparación, inventario en
  curso); queda en preparación en la tienda hasta Terminar. Sin exceso: 200 con Lineas vacía, «No hay exceso…», nada creado.

### Diferencias de reposición «En discusión» y quién pierde (NestoAPI#906, sugerencia 1433, Carlos 25/11/26: decide el sistema, no Dirección)
- Solo FALTAS pendientes: el ORIGEN (permiso de resolver) abre `POST api/Reposiciones/Diferencias/{id}/Discusion` {Comentario obligatorio}; campana al
  destino (tipo `DiscusionDiferenciaReposicion`); plazo ParametrosUsuario «(defecto)» `Reposiciones.DiscusionPlazoHoras` (48), recordatorio 24 h antes.
  El destino (su gente, Almacén, Dirección) contesta `…/Discusion/Respuesta` {Respuesta LoHeEncontrado|NoEsta, Comentario?}: «Lo he encontrado» = la
  resolución ErrorRecepcion de siempre (error RECONOCIDO del destino); «No está» o plazo vencido (job `reposiciones-discusiones-diferencias`, cada hora 8-21)
  → decide LA REGLA (pura) `ReglaImputacionDesacuerdo` (`DesacuerdoDiferenciasReposicion.cs`), por unidad (nunca se parte): 1) prueba: lo no leído por
  ESCÁNER al preparar (PreparacionEscaneos REPO PICK/PACK SCAN; lo tecleado no cuenta) → origen; lo leído con TODOS los bultos R{t}-{n} leídos al recibir
  (ReposicionesBultos.FechaLectura) o «Entregado» (RepartoBultosMovimientos) → destino; 2) si no, peor indicador de errores demostrados (12 meses); 3)
  empate o sin datos: alternancia por pareja (la primera, al origen). Resolución nueva «Desacuerdo» (no elegible en Resolver) y la baja por el cauce de
  siempre (_ErrRepo, `PlanStockDesacuerdo`: textos «Desacuerdo de reposición N (pérdida|llegó|no vuelve|vuelve)») en el almacén imputado; interruptor de
  ajuste apagado = no toca stock. Si el origen resuelve como siempre con la discusión abierta: Acordada (Confirmada/NoEstaba = error reconocido del origen).
- Errores demostrados (`ReglaIndicadorErrores`): ErrorRecepcion y Sobraba → destino; Confirmada y NoEstaba → origen; en desacuerdos solo el paso Prueba o la
  verdad. `GET …/Diferencias/Indicador?meses=12` (Dirección, Informática, Almacén; también por usuario). `GET …/Diferencias/Discusiones?almacen=&soloAbiertas=`.
- Datos para un modelo (en sombra): `dbo.DiferenciasReposicionDiscusiones` + `dbo.DiferenciasReposicionImputaciones` (una fila por unidad con rasgos, decisión
  y verdad; `Scripts/Issue906_DiferenciasReposicionDiscusiones.sql`, amplía la CHECK de Resolucion; sin él, 503 y todo como antes). Job `reposiciones-desacuerdos-verdad`
  3:35 (cola «indicadores»): inventario contabilizado posterior con sobrante ≥ unidades en origen O destino (`ReglaVerdadInventario`: cada fila gasta una vez;
  en los dos, no decide; no pisa) → ErrorDemostradoDe/Inventario, ReglaAcerto; si contradice, campana a Dirección, sin deshacer. `GET …/Diferencias/Verdad?meses=24`
  (Dirección/Informática). Modelo preparado sin implementar (`ModeloImputacionDesacuerdo`, interruptor `Reposiciones.DesacuerdoQuienManda`, hoy siempre la regla).

### Excepciones del calendario de reposiciones (NestoAPI#867, 16/11/26)
- `dbo.ReposicionesCalendarioExcepciones` (fuera del EDMX, `Scripts/Issue867_ReposicionesCalendarioExcepciones.sql`; sin ella todo como antes y los
  endpoints 503): una fila por empresa + origen + destino + día de LLEGADA, Tipo `SinReposicion` (quita ese día) o `ReposicionExtra` (añade uno, con
  HoraCierre/LaborablesAntelacionCierre/HoraLlegadaHabitual). LA REGLA: `ReglaCalendarioReposiciones.ViajesDelDia` (pura); la usa
  `CalculadoraFechaReposicion` (próxima llegada, fechas de entrega, cortes del job: un día quitado ni se rellena ni avisa «fuera de plazo»). Una extra
  solo cuenta en una ruta con alguna fila activa; festivos y días sin reparto propio siguen mandando. Caché `ExcepcionesCalendarioReposiciones`
  (5 min, `Invalidar` al escribir; la calculadora la lee sola si no se le inyectan).
- `GET/POST api/Reposiciones/Calendario/Excepciones` (de hoy en adelante; POST = crear o cambiar por la clave) y `DELETE …/{id}`, con el permiso de
  PUT Calendario. 400: fecha pasada, quitar un día que el calendario no tiene (o festivo), extra en un día que sí tiene o con otra antelación; 409 si
  la hora de cierre del viaje afectado ya ha pasado. Rastro en Modificaciones. Nesto: sección «Excepciones» en la ventana del calendario.

### Festivos en tabla (NestoAPI#779, 04/11/26)
- `GestorFestivos.EsFestivo` (remesas, fechas de entrega, picking, `JobsFestivos`, avisos…) lee los festivos del año de
  `CalendarioFestivos.DelAnio`: tabla `dbo.Festivos` (Fecha, Tipo, Descripcion, Usuario, FechaModificacion; fuera del EDMX;
  `Scripts/Issue779_Festivos.sql`), caché de 10 min, `Invalidar` al escribir. Tipos = enum `TipoFestivo`; las reglas por delegación no cambian.
- Por qué «Algete» NO cuenta para ALG (Carlos 04/11/26): en Algete unos empleados cogen las fiestas de Algete y otros las de Madrid, pero el
  almacén abre; Algete solo cierra en nacionales y autonómicas. Reina cierra en las locales de Madrid y Alcobendas en las de Alcobendas.
- Respaldo: sin tabla → la lista del código (`FiestasDelCodigo`, sin ELMAH); tabla sin filas del año → ELMAH una vez por año y la
  lista del código de ese año (o nada). La lista del código se quita cuando la tabla esté en producción.
- `GET api/Festivos?anio=`, `POST api/Festivos`, `DELETE api/Festivos/{fecha}/{tipo}` (escribir: Administración/Dirección/Informática).
  Job `festivos-sin-cargar` (día 1 de cada mes; en diciembre mira el año siguiente) avisa a Dirección (campana + correo).
- **Día sin reparto propio (NestoAPI#841, 09/11/26)**: tipo `RepartoPropio` (no sale la furgoneta). NO es festivo de ningún almacén
  (`EsFestivo(fecha, ALG/REI/ALC)` lo ignora); `GestorFestivos.EsDiaSinRepartoPropio` y LA REGLA del reparto propio
  `GestorFestivos.EsFestivoRepartoPropio` (= `EsFestivo(fecha, GestorFestivos.REPARTO_PROPIO)`: festivo de Algete o sin reparto).
  Frena AT/16 (fecha de entrega, picking de rutas internas, RP, paradas), las llegadas de reposición (`CalculadoraFechaReposicion`) y el
  aviso de las 22:00 de la furgoneta. Lo dan de alta también Almacén (solo ese tipo; no hay grupo «Logística» en el dominio, Carlos 09/11/26). Requiere `Scripts/Issue841_FestivosRepartoPropio.sql` (CHECK).

### Años disparatados en el calendario (12/11/26)
- `CalendarioFestivos.DelAnio` de un año anterior a 2000 o de más de 5 por delante del actual (`EsAnioDisparatado`: las centinela 31/12/2099,
  2100…) → sin festivos, sin ELMAH y sin leer la tabla (los fines de semana siguen contando). El aviso del año que viene sin cargar no cambia.
  Las fotos de `fechas-entrega-fotos-sombra` guardan esas fechas como «sin fecha» (`ReglasFotosFechaEntrega.SinFecha`).

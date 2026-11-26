# Almacén: códigos de barras, etiquetas y recorrido

Documentación de NestoAPI para Claude, por temas (el índice está en `CLAUDE.md`). Código interno, muestras, impresoras de etiquetas, inmovilizados, calles del recorrido, avisos de ficha.
Lo nuevo de este tema se apunta aquí, en su sección o en una nueva; en `CLAUDE.md` solo se toca su línea del índice.

### Código de barras interno y recibir escaneando (NestoAPI#762, 30/10/26)
- Todo producto sin código de barras recibe uno **interno**: EAN-13 del rango 29 (`CodigoBarrasInterno`): `290` + referencia a 9
  cifras + control (17404 → 2900000174047) o `299` + contador para referencias no numéricas o con choque. Va en
  `ProductosCodigosBarras` (Origen `Almacen`) y **nunca es el principal** (la ficha `Productos.CodBarras` la manda PrestaShop como GTIN).
  Se da en exclusiva (`sp_getapplock` en `RepositorioCodigosBarras.GuardarInternoEnExclusiva`). En la BD nadie usaba el 29.
- Etiqueta 3×2 EAN-13: `POST api/Almacen/EtiquetasProducto/Imprimir`. Desde NestoAPI#791 es la ÚNICA etiqueta de producto
  (`Formato` se acepta y se ignora; ya no hay Code128 con la referencia). Lleva el EAN-13 de la unidad (Cantidad 1) o, si no
  tiene, el interno, que se le da al imprimir (`CodigoBarras`, `CodigoInterno`, `CodigoAsignado` en la respuesta).
  Medidas en `GeneradorEtiquetasProducto.EAN_*`; prueba con `Scripts/Issue762_EtiquetaPrueba.ps1`.
- **Elegir la impresora; por defecto, la del ALMACÉN (Carlos 25/11/26)**: LA REGLA (pura) `ReglaImpresoraEtiquetas` (`PreparacionAlmacen/ImpresoraEtiquetas.cs`):
  (1) la pedida; (2) la del almacén del trabajo (ALG/REI/ALC): ParametrosUsuario empresa 1 «(defecto)» `ImpresoraEtiquetas.{ALG|REI|ALC}` (leído SIN crear
  filas, `LectorDefecto`) o, sin fila, etiquetas2/etiquetasreina/etiquetasalcobendas; (3) sin almacén u otro, la ImpresoraCodBarras del usuario. Devuelve
  `Origen` «Pedida»/«Almacen»/«Usuario». La pedida llega del cliente y acaba en un OpenPrinter: `MotivoNoValida` exige `\\servidor\cola` (sin más barras,
  «..» ni caracteres raros, ≤ 100) en un servidor CONOCIDO (el de alguna impresora de almacén o del usuario; RDS2016 siempre) → 400 de usted.
  Muestras: `Impresora` opcional en la petición; sin ella, la del `Almacen` pedido (antes, la del usuario: Carlos en ALC imprimía en Algete).
  Producto (`Imprimir`): `Impresora` y `Almacen` opcionales; SIN ninguno, la del usuario como siempre (Nesto y Ariadna no notan nada). Las dos respuestas
  traen `Impresora` + `OrigenImpresora`. `GET api/Almacen/EtiquetasProducto/Impresoras` (permisos de imprimir) → [{Almacen, Nombre, Impresora,
  EsLaDelUsuario}]: las tres de almacén y, si es otra, la del usuario (Almacen null, «La suya»). Inmovilizados delega en la común con su clave
  antigua `ImpresoraEtiquetasInmovilizado.{…}`, que gana sobre la general.
- Las REPO se reciben solo escaneando (`RecepcionDTO.SoloEscaneando`); lo que llega sin etiqueta, unidad a unidad con
  `POST api/Almacen/Recepciones/{tipo}/{documento}/SinEtiqueta` (avisa al origen y a Compras; la unidad la cuenta la app).
  Ariadna#92: si el producto tiene código de envase (ficha o ProductosCodigosBarras activo, sin contar los internos 29), `NoLleva`
  → 400 «léalo del envase» y `NoSeLee`/`Otro` avisan SOLO a Compras (`IAvisadorSinEtiqueta.AvisarACompras`).
- Limpieza: `GET api/Productos/RepuestosSinCodigoBarras?meses=3` (o `Scripts/Issue762_ProductosRepuestosSinCodigo.sql`).
- **Muestras sin código (NestoAPI#797, 03/11/26)**: las muestras (subgrupo MMP) sin código NO se etiquetan. Toda línea con `SinCodigo`
  (recorrido, packing, Ubicar, Recibir COMP/REPO, y Enviar reposición) lleva `SinCodigoAdmitido` = sin código Y MMP (regla única
  `MuestrasSinCodigo.Admitido`; cada consulta trae `Productos.SubGrupo`): se teclea, sin aviso. `SinEtiqueta` de una línea admitida
  cuenta y no llama a `IAvisadorSinEtiqueta`; `RepuestosSinCodigoBarras` y su script excluyen MMP. Imprimir etiqueta de una muestra sigue valiendo.
- **Muestras: etiqueta en el cajón o la gaveta (NestoAPI#884, Carlos 24/11/26)**: la muestra sin código de fabricante NO se etiqueta
  unidad a unidad: UNA etiqueta por referencia en el cajón del mostrador (REI, ALC) o en la gaveta / el hueco (ALG), con el interno del 29.
  Leerla es la vía cómoda, NO obligatoria: `MuestrasSinCodigo.Admitido` mira la ficha (`CodBarras`, donde el interno nunca va), así que
  la muestra cuyo único código es el interno sigue «admitida» (se teclea sin aviso). Todo en `MuestrasContenedor.cs` (reglas puras
  `ReglasMuestras`, lectura en 3 consultas `RepositorioMuestrasSql`) y `MuestrasController`: (a) `POST api/Productos/Muestras/CodigoInterno?ensayo=true`
  (por defecto ensayo: cuántas y cuáles; de verdad, cada una por `AsignarInterno` → `GuardarInternoEnExclusiva`, idempotente, una que
  falla no para las demás; una fila en Modificaciones Tabla `CodigoInternoMuestras`): muestras activas sin NINGÚN GTIN válido activo
  (ficha o tabla, el interno incluido); Almacén, Compras, Dirección, Informática. (b) `POST api/Almacen/EtiquetasProducto/Muestras?ensayo=true`
  {Almacen (ALG/REI/ALC), Familia (código o nombre), SoloConStock, Productos[]}: la MISMA 3×2 y la misma impresora que la de producto,
  con «MUESTRA · Ref. X» en pequeño y el nombre en grande (sin la palabra «MUESTRA»), EAN-13 a 54 puntos (`CONTENEDOR_*`); el código, el
  de la unidad (`CodigoDeLaUnidad`: fabricante, si no el interno); en ALG una por hueco con existencias (Ubicaciones 0/3/4) por
  `OrdenRecorrido` y detrás las sin hueco; en tiendas por familia y nombre; UN trabajo, 500 como mucho; de verdad, a la que no tiene se le
  da el interno DESPUÉS de mirar la impresora. (d) `GET api/Productos/Muestras/Hoja?almacen=&familia=&soloConStock=&conFotos=true` →
  PDF (QuestPDF, `HojaMuestras.cs`): por familia, 3 por fila, foto de la tienda (`IFotosProductoAlmacen` + descarga en paralelo, la que
  no llega no sale), nombre, referencia y EAN-13 dibujado (`Ean13`, como `Code128`); 404 sin muestras. (b) y (d): los permisos de las
  etiquetas de producto (también Tiendas). (c) `PideCantidad` (aditivo, = MMP, `MuestrasSinCodigo.PideCantidad`): un bip de una muestra
  NO es una unidad, la app pide la cantidad. Va en `ProductoPorCodigoBarrasDTO` (PorCodigoBarras y los 409 de candidatos), `Inventario`
  (GET/POST/PUT, `Inventario.Partial.cs`), líneas de recorrido/picking y salida de reposición, packing, Ubicar, Recibir COMP/REPO, Enviar
  reposición y reposición puntual. No en Productos/Buscar ni en la Ficha de Ariadna. Gemelas: Nesto#650, Ariadna#108.
- **Picking: leer la etiqueta de la muestra, obligatorio por almacén (Carlos 25/11/26)**: interruptor por DATO ParametrosUsuario empresa 1 «(defecto)»
  `Muestras.PickingLecturaObligatoria.{ALG|REI|ALC}` (SOLO «1» = encendido; sin fila, apagado y todo como hoy; nace apagado; `InterruptorLecturaMuestras`,
  singleton, caché 2 min, nunca lanza). Solo en el RECORRIDO de recogida (`GET api/Almacen/Recogidas/{PICK|REPO}/{n}` sin `ligero`, y `GET Picking/{n}`),
  después de completar los códigos: LA REGLA (pura) `ReglaLecturaObligatoriaMuestras.Aplicar` (`LecturaObligatoriaMuestras.cs`): muestra con algún GTIN
  válido activo (ficha o tabla, el interno incluido) → `LecturaObligatoria` = true (aditivo), `SinCodigoAdmitido` = false y, sin código en la ficha,
  `CodigoBarras` = su primer código y `SinCodigo` = false; `PideCantidad` sigue. Sin ningún código, como hoy. Packing, Ubicar, Recibir, Enviar reposición
  de tienda (`api/Reposiciones/...`, otro flujo) e inventario: sin cambios. Escape: `POST api/Almacen/Recogidas/{tipo}/{n}/MuestraSinEtiqueta`
  {Producto, Pasillo, Fila, Columna, Motivo NoLleva|NoSeLee|Otro, Comentario} (no cuenta: la unidad la cuenta la app a mano tras el 200; 400 si no es muestra
  o el almacén no lo exige): campana de Nesto `MuestraSinEtiquetaEnHueco` {producto, hueco, almacen, usuario} a Almacén (y la gente de la tienda si es
  REI/ALC), NUNCA a Compras, una por almacén + producto + hueco y día (en memoria). `GET/PUT api/Almacen/Muestras/LecturaObligatoria` (Almacén, Dirección,
  Informática): [{Almacen, Encendido, MuestrasConCodigo, MuestrasSinCodigo}] (con stock allí); PUT {Almacen, Encendido} con rastro en Modificaciones
  (Tabla ParametrosUsuario), invalida la caché y, al encender, `Aviso` con las que siguen sin código. Ariadna necesita usar `LecturaObligatoria`
  (hoy en el recorrido se puede teclear la referencia de cualquier producto con código) y el endpoint de escape.

### Etiquetas de inmovilizado (Carlos 25/11/26; gemela Nesto)
- Máquinas inmovilizadas tras el inventario (demostración, uso propio): etiqueta 3×2 «INMOVILIZADO · NO ES STOCK», «I/1823» en grande con la
  delegación, la descripción en hasta 3 líneas y un Code128 con el texto EXACTO «I/1823» (56 puntos de alto). La descripción, por LA REGLA
  `ReglaDescripcionEtiquetaInmovilizado` (sin cortar palabras): el número de serie («n/s», «s/n», «nº serie», hasta el final) sale SIEMPRE entero;
  si no cabe, se recorta el nombre con «…» y, después, la referencia. LA REGLA `CodigoInmovilizado` (`Infraestructure/Inmovilizados/`):
  `Componer` («I/» + número sin relleno) y `Leer` («I/1823», «i/1823», espacios; solo cifras; el número a secas NO). Tabla Inmovilizados (EDMX), solo lectura.
- `GET api/Inmovilizados?empresa=1&filtro=&delegacion=&soloVivos=true` (`InmovilizadosController`; cliente de la tienda 403; 500 como mucho, los más nuevos
  primero; filtro en número —«I/1823» = ese exacto— y descripción) → [{Empresa, Numero, Codigo, Descripcion, Grupo, Delegacion, Estado, EstadoDescripcion,
  FechaCreacion}]. `POST api/Inmovilizados/Etiquetas/Imprimir` {Empresa?, Numeros 1..50, Copias 1..10, Impresora?} (Administración, Dirección, Almacén,
  Tiendas, Informática; 404 si no existe ninguno) → {Impresas, Etiquetas [{Numero, Codigo, Descripcion, Delegacion, Impresora, Ok, Motivo}]}; un trabajo
  por impresora por `ImpresoraEtiquetasConAjuste` (EPL/ZPL y ajuste, como la de producto). Impresora: `ReglaImpresoraInmovilizado` = la común `ReglaImpresoraEtiquetas` (la pedida; si no, la
  fila «(defecto)» `ImpresoraEtiquetasInmovilizado.{ALG|REI|ALC}`, después la general `ImpresoraEtiquetas.{…}` —sin crear filas— o etiquetas2/etiquetasreina/etiquetasalcobendas; VAR u otra, la
  ImpresoraCodBarras de quien imprime).
- Leerla donde se busca un producto (`ReglasInmovilizados.Reconocer`, solo lee la BD si tiene el formato; nunca casa con el producto 1823): cuerpo
  `EsInmovilizadoDTO` {Codigo «ES_INMOVILIZADO», Message «I/1823 es un inmovilizado (…): no es stock, no se cuenta ni se prepara.» / «… tiene formato de
  inmovilizado, pero no existe.», Inmovilizado, Productos []}. 409 en GET/POST api/Inventarios (sus clientes ya leen Codigo + Message de un 409); 404 en
  GET api/Productos?codigoBarras= (su 409 es una LISTA: la app de clientes y Ariadna la deserializan) y en la ficha de Ariadna (también
  `Productos/I/{numero}/Ficha`, por la barra escapada). PorCodigoBarras y Productos/Buscar (200 con lista) no cambian: dan la lista vacía.

### Mantenimiento de calles del recorrido (NestoAPI#642, corte 4 de #639, para Nesto#528, 12/11/26)
- `GET api/Almacen/Recorrido?almacen=ALG&empresa=1` → `RecorridoAlmacenDTO`: calles (Orden, Sentido A/D, pasillos con Posicion, Nombre y
  Huecos), `PasillosSinCalle` (con huecos en uso o con nombre) y `Tramos` (pasillo+columna+nivel AMano/Escalera de los huecos en uso, con la
  regla única `OrdenRecorrido`). Sin las tablas de `Scripts/Issue641_RecorridoCalles.sql`: `TablasCreadas` false y el orden de siempre.
- `PUT …/Recorrido?almacen=&empresa=&ensayo=` sustituye TODO el recorrido del almacén en una transacción (`RepositorioRecorridoSql.Guardar`):
  el orden de la lista es el de las calles (Orden 10, 20…), el de cada lista de pasillos su Posicion; `FilaAMano` null = no tocar. Permiso: el
  de Etiquetas de hueco (escribir en api/Almacen: Almacén o Dirección). 400 validación, 403, 503 sin tablas. Sin caché: el lector es Scoped.

### Aviso de ficha desde Ariadna: foto del mozo (NestoAPI#868, 16/11/26)
- `POST api/Almacen/AvisosFicha` admite `FotoMozoBase64` (opcional, JPEG, tope 4 MB; `FotoMozoAvisoFicha.Leer`, 400 si no vale): se sube UNA vez al blob de
  bultos (`avisos-ficha/{producto}-{guid}.jpg`) y su ruta va en `AvisosFichaProducto.FotoMozo` (`Scripts/Issue868_AvisosFichaFotoMozo.sql`; sin la columna o
  sin blob, no se guarda pero la foto va igual en el correo). Correo: «La foto de la tienda que ha visto» (cid foto-ficha) y «Lo que tiene el mozo en la
  mano» (cid foto-mozo). Varios informantes: la última foto (sin foto nueva se queda la guardada; el correo solo lleva la del envío).

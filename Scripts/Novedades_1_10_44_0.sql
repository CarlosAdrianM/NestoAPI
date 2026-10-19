/*
    Novedades de la versión 1.10.44.0 de Nesto (19/10/2026).
    Versión de la ClickOnce: 1.10.44.0 (pubxml 1.10.44.* en revisión 0: recibir una reposición por partes, «Dar por cerrado» en
    Incidentados, avisos a los comerciales de los retenidos y de las incidencias de agencia).
    Ariadna NO entra aquí: la 2.13 tiene su propio script (Novedades_Ariadna_2_13.sql).

    Tanda: NestoAPI (#665 correo de retenidos al comercial [sugerencia 584], #666 incidencia de agencia → correo, campana y push
    a NestoApp [sugerencia 672], #667 sin stock mínimo en las tiendas, #672 ajuste de las etiquetas por impresora, #673 recibir
    por partes + aviso diario, #674 códigos de barras validados + informe semanal a Compras, #675 etiqueta de bulto con cualquier
    separador, #676 «Dar por cerrado» con estado propio «Cerrado a mano» [sugerencia 673], #593 los ficticios (cheque, cupón,
    TiCKET) fuera del recorrido y del packing de Ariadna, #552 GLS fase 3 [interruptor APAGADO], #670 sugerencias de productos
    corte 1 [sin pantalla todavía], Ariadna#43/#47 en la API) + Nesto (#538 código que no venía, recibir por partes, «Dar por
    cerrado», etiqueta de bulto con cualquier separador, #490 4C.5 tramo 3a sin cambio visible).

    ORDEN:
      1) Scripts sa ANTES de publicar la API:
         - Issue667_MinimosTiendasACero.sql           (mínimos de REI y ALC a 0; debe actualizar unas 1.375 filas)
         - Issue670_SugerenciasProductos.sql          (tablas de las sugerencias y el interruptor del trabajo nocturno)
         - Ariadna43_LecturasYMotivosTecleo.sql       (OBLIGATORIO antes de Ariadna 2.13: CHECK de Metodo con ETIQ/TECLA y dos
                                                       tablas; si Ariadna sale antes, la API rechaza las lecturas)
         - (Ya lanzado: Issue672_ImpresorasEtiquetasAjuste.sql)
         - OPCIONAL, de noche o en fin de semana: Ariadna47_IndiceUbicacionesHueco.sql
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.44.0.
      4) DESPUÉS: ESTE script (idempotente: compara por Versión + Título) y OneShot_20261019_CerrarSugerencias_1_10_44_0.ps1.

    SE OMITE A PROPÓSITO:
      - Sugerencias 584 (retenidos al comercial), 672 (incidencias de agencia) y 673 («Dar por cerrado»): van por la propia
        sugerencia (PUT con su versión), no aquí.
      - #672 ajuste por impresora: solo afecta a la impresora de Reina; se le cuenta en su hilo (653).
      - #552 GLS fase 3 y #670 sugerencias de productos: sin cambio visible (interruptor apagado / sin pantalla).
      - #490 4C.5 tramo 3a: sin cambio visible.
      - Lo de Ariadna (cámara, tres tipos de lectura, productos parecidos, cheques fuera del recorrido…): en su script.
      - En la factura, si el vendedor de estética y el de peluquería son el mismo, se nombra una sola vez: menor.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.44.0';
DECLARE @fecha DATE = '2026-10-19';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Recibir una reposición por partes',
     N'En Recibir reposición hay un botón nuevo, «Dar de alta solo lo leído». Sirve, por ejemplo, cuando una clienta viene a recoger algo que llega en la reposición y no da tiempo a escanearla entera: lea solo lo que necesita y púlselo. Se da de alta lo leído y el resto de la reposición queda pendiente para recibirlo después, sin contar como falta. La reposición sigue en la lista con «Recibido en parte». Si una se queda a medias más de dos días, le llegará un aviso a la campana cada mañana hasta que la termine.',
     N'Tiendas,Almacén'),
    ('Mejorado', N'Recibir reposición: lo que no venía se apunta con su producto',
     N'Si escanea algo que no venía en la reposición, Nesto busca de qué producto es y lo apunta con su referencia y su nombre («no venía en la reposición»). Antes se apuntaba el código de barras tal cual y daba error al terminar. Si el código no es de ningún producto, le avisa sin apuntarlo. Además, la etiqueta del bulto se reconoce aunque la pistola cambie el guion por otro signo.',
     N'Tiendas,Almacén'),
    ('Mejorado', N'Las tiendas ya no tienen stock mínimo',
     N'El stock mínimo es solo de Algete; en las tiendas solo hay máximo. «Actualizar stocks por proveedor» ya no graba un mínimo cuando el almacén es Reina o Alcobendas, y los mínimos que se habían puesto en las tiendas se han quitado. Los máximos se quedan como estaban.',
     N'Compras'),
    ('Mejorado', N'Códigos de barras: solo se guardan códigos válidos, y un informe semanal para Compras',
     N'Al añadir un código de barras a un producto, si no es un código válido (por ejemplo «0» o «1», o un EAN con alguna cifra mal) se le avisa y no se guarda. Los códigos así que ya había no se usan al leer. Y cada lunes por la mañana Compras recibe un correo con los productos sin código, con un código que no es válido, con un código que comparten varios productos y con un código que en el almacén no se lee bien, para irlos corrigiendo.',
     N'Compras');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

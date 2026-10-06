/*
    Novedades de la versión 1.10.37.0 de Nesto (06/10/2026) y de la 2.1 de Ariadna.
    Versión de la ClickOnce: 1.10.37.0 (pubxml 1.10.37.* en revisión 0). Ariadna: ApplicationDisplayVersion 2.1.

    Tanda: NestoAPI (reposiciones #553 en los dos sentidos + puerta de Ubicaciones #594, agencias #595 s1/s4 y #597,
    códigos postales #596, TiCKET 501, vencidas #549) + Nesto (Enviar reposición, 505, #510, #511, #597, 4C.4) +
    Ariadna 2.1 (cámara 500, reposición a tienda #17, #15, #16).

    ORDEN:
      1) Publicar NestoAPI (no hay scripts sa previos).
      2) Publicar la ClickOnce (Clean + Rebuild) y lanzar los flujos de Ariadna (Play interna y Windows).
      3) DESPUÉS: ESTE script (idempotente: compara por Versión/Ámbito + Título).
      4) Marcar implementadas las sugerencias 500 (2.1), 501, 505, 508, 509 y 510 (1.10.37.0) por la API
         (PUT api/Novedades/Sugerencias/{id}); NO se insertan aquí para no duplicarlas.

    SE OMITE A PROPÓSITO:
      - Modernización Nesto#490 (Agencias y Canales Externos sin Prism): no cambia nada para el usuario.
      - Propuesta de envío en sombra e imprimir etiqueta desde el servidor (#595): todavía sin pantalla.
      - Reposición desde Algete por la API: se cuenta en Ariadna, que es desde donde se crea.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.37.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-10-06';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Mandar la reposición a Algete desde la tienda, sin Nesto viejo',
     N'En Productos › Reposición › «Enviar», pulsa «Preparar reposición» y Nesto propone lo que hay que mandar a Algete. Puedes bajar cantidades o ponerlas a cero (nunca subirlas), leer un código con el lector para ir a su línea, y con «Terminar y mandar a Algete» se cierra la reposición con su número de traspaso y Algete la ve como pendiente de recibir. Ya no hace falta hacerla en el Nesto viejo.'),
    ('Mejorado', N'El código postal vale como lo escribas',
     N'Los códigos postales portugueses se aceptan con guion, con espacio o seguidos (4480-670, 4480 670 o 4480670) y Nesto los guarda siempre igual; a cada agencia se le mandan en el formato que necesita. Antes un código con espacio hacía que CTT no tramitara el envío.'),
    ('Mejorado', N'Modificar un envío ya registrado en la agencia reenvía el cambio',
     N'Si cambias el retorno, el reembolso o el servicio de un envío que ya tiene etiqueta, Nesto se lo manda a la agencia: Innovatrans lo modifica y CTT anula el albarán y hace uno nuevo, que se imprime solo. En GLS el cambio viaja en el cierre del día si el envío aún no se ha cerrado; si ya se cerró, hay que pedírselo a la agencia, y Nesto lo dice. Un envío ya entregado no se puede cambiar: el siguiente albarán hace envío nuevo.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;

-- Ariadna 2.1 (la cámara a pantalla completa es la sugerencia 500 de Alfredo: se marca implementada por la API)
DECLARE @ariadna TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @ariadna (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Reposición a tienda desde la PDA',
     N'En el menú, «Reposición a tienda» (tecla 8): eliges Reina o Alcobendas, ves la propuesta con sus líneas y unidades, y con Intro se crea. Al momento sale en Salidas como una reposición más para recogerla por huecos y terminarla desde la PDA. Si te has equivocado, justo después de crearla, F3 la anula.'),
    ('Corregido', N'Ubicar y Recibir dicen «Cargando…» mientras leen la lista',
     N'Al entrar, salía «Nada pendiente de ubicar» antes de que el servidor contestara. Ahora se ve que está cargando y el aviso de lista vacía solo sale cuando de verdad no hay nada.'),
    ('Nuevo', N'La versión instalada se ve al pie del inicio',
     N'En la pantalla de inicio, abajo, sale la versión y la compilación que tienes instalada (por ejemplo «Ariadna 2.1 · compilación 40 · Android»), para saber si estás al día cuando salga una novedad.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.1', @fecha, a.Categoria, a.Titulo, a.Descripcion, 'Ariadna', 1, 'sa'
FROM @ariadna a
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = a.Titulo);

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

-- Comprobación: 3 filas de Nesto con esta versión y 3 de Ariadna con la 2.1.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version IN (@version, '2.1') ORDER BY Id;

/*
    Novedades de Ariadna 2.36 (13/11/26): «Enviar reposición» solo cuenta lo LEÍDO (Novedades 1264, Dirección 13/11/26).
    El 13/11/26 un producto sin código de barras se comprobó tecleando su referencia, se imprimió su etiqueta y no se pegó:
    en Algete, donde solo se recibe leyendo, no se pudo leer.
    No necesita API nueva ni script. Ejecutar en SSMS contra NV como sa cuando la 2.36 llegue a los móviles de las tiendas.
    Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Enviar reposición: teclear la referencia ya no cuenta, hay que leer el código de barras',
 N'Al preparar la reposición para Algete, cada producto se comprueba leyendo su código de barras con el lector o la cámara. Si teclea la referencia, Ariadna le lleva a ese producto (para ver su foto o imprimir su etiqueta con TAB), pero no lo da por comprobado ni cuenta unidades. Si el producto no tiene código de barras, imprima su etiqueta, péguela y léala: hasta que no se lee la etiqueta pegada, no cuenta. Una vez comprobado leyendo, la cantidad se sigue tecleando como siempre. Las muestras sin código siguen igual. Si tenía una reposición a medias al actualizar, lo contado antes no vale: Ariadna se lo dice una vez y hay que volver a leerlo.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.36', '20261113', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Ambito = 'Ariadna' AND Version = '2.36' ORDER BY Id;

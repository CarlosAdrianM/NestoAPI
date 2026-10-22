/*
    Novedades de Ariadna 2.15 (22/10/26): Ariadna#50 (segunda confirmación al enviar una reposición con poco leído) y
    NestoAPI#685 (la etiqueta de bulto leída con la cámara, con prefijo AIM, se reconoce).
    Va primero al canal INTERNO (Carlos). Ejecutar en SSMS contra NV como sa cuando la 2.15 llegue a la prueba cerrada.
    Idempotente (compara por Ámbito + Título).
    Al publicar: comentario con @Paloma @Reina (tiendas) sobre la segunda confirmación, y @Andre sobre la etiqueta con la cámara.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Mejorado', N'Enviar reposición: aviso si falta mucho por leer',
 N'Si al enviar una reposición ha leído menos de la mitad de los productos, Ariadna le pregunta otra vez («Solo ha leído 2 de 24 productos…»). Lo que queda marcado es «Seguir leyendo» (Intro o 1); para enviar solo lo leído hay que pulsar 2 o tocar «Enviar solo lo leído». Así un Intro o un F4 de más no la envía sin querer.',
 N'Tiendas'),
(2, N'Corregido', N'La etiqueta del bulto se lee también con la cámara',
 N'Al recibir una reposición, la etiqueta del bulto leída con la cámara del móvil a veces no entraba y había que teclearla. Ya se reconoce.',
 N'Almacén');
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.15', '2026-10-22', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

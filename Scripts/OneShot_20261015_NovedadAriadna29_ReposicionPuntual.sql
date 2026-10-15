/*
    Novedad de Ariadna 2.9 que faltaba en Novedades_1_10_42_0.sql (Ariadna#34 entró después de lanzarlo).
    Ejecutar en SSMS contra NV como sa DESPUÉS de publicar Ariadna 2.9. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.9', '2026-10-15', 'Nuevo', N'Reposición puntual de Algete a una tienda con solo los productos que diga',
       N'En Salidas, F7 (Reina) o F8 (Alcobendas) ahora preguntan «¿Qué lleva?»: 1 «Propuesta completa» (lo de siempre) o 2 «Solo unos productos». Con la segunda, lea el código de cada producto (o teclee su referencia): cada lectura suma una; la cifra + F2 cambia la cantidad (0 la quita) y F3 quita la línea. Intro sin nada o F4 la crea, reserva los huecos y la abre para prepararla como cualquier otra. Sirve para mandar algo suelto que pide una tienda fuera del calendario. Solo para Manuel, Alfredo y Carlos.',
       'Ariadna', 1, 'sa'
WHERE NOT EXISTS (SELECT 1 FROM Novedades WHERE Ambito = 'Ariadna' AND Titulo = N'Reposición puntual de Algete a una tienda con solo los productos que diga');

SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

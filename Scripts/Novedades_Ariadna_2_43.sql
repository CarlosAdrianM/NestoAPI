/*
    Novedades de Ariadna 2.43 (24/11/2026): pantallas en móviles pequeños con la letra grande (Ariadna#107). Ya publicada en prueba cerrada.
    No necesita nada nuevo de la API.
    Ejecutar contra NV como sa. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.43', '20261124', 'Mejorado', N'Móviles pequeños con la letra grande: se ve la lista y los botones enteros',
       N'En los móviles pequeños con la letra grande, la lista deja siempre sitio para dos o tres filas (si no cabe, se desplaza), los botones se ven enteros y la cámara tiene sus botones a la vista, con «Dato mal» encima cuando el código no casa. Sin teclado físico, los avisos dicen qué tocar («Toque «Dato mal»…») en vez de qué teclas pulsar. Las PDA no cambian.',
       'Ariadna', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = N'Móviles pequeños con la letra grande: se ve la lista y los botones enteros');
SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

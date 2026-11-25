/*
    Novedades de Ariadna 2.44 (24/11/2026): muestras, un bip no es una unidad (Ariadna#108, gemela de NestoAPI#884). Ya publicada en prueba cerrada.
    Necesita la API del 24/11/26 (ya publicada). Va con la ventana de Muestras de Nesto 1.10.67.0 (la etiqueta del cajón o la gaveta).
    Ejecutar contra NV como sa. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.44', '20261124', 'Mejorado', N'Muestras: al leer la etiqueta del cajón o la gaveta, Ariadna pregunta cuántas',
       N'Las muestras sin código del fabricante llevan una sola etiqueta por referencia, en el cajón del mostrador o en la gaveta, no en cada unidad. Por eso leer una muestra ya no cuenta una unidad: Ariadna pregunta cuántas (escriba la cifra e Intro, o use − / + / «Todas»; Esc no cuenta nada). Vale para el recorrido, el packing, enviar y recibir una reposición, ubicar y el inventario. Una muestra sin etiqueta se sigue pudiendo teclear.',
       'Ariadna', 1, 'sa', N'Almacén,Tiendas'
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = N'Muestras: al leer la etiqueta del cajón o la gaveta, Ariadna pregunta cuántas');
SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

/*
    Novedades de Ariadna 2.42 (23/11/2026): inventario desde el móvil (Ariadna#105, corte 1). Ya publicada en prueba cerrada.
    Ejecutar contra NV como sa. Idempotente.
*/
SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.42', '20261123', 'Mejorado', N'Inventario: leer el producto, «¿Cuántas hay?» y contabilizar',
       N'La pantalla de inventario es nueva. Lea el código del producto (o teclee la referencia) una sola vez: se abre su ficha con la pregunta «¿Cuántas hay?». Escriba el TOTAL que hay y pulse «Guardar» (Intro en la PDA); ya no suma una unidad por cada lectura. Lo contado se ve debajo, en «Contado hoy», y tocando una fila se puede cambiar. Al acabar, el botón «Terminar y contabilizar» enseña lo que va a cambiar y lo contabiliza: no hace falta ir a Nesto. Mientras se cuenta no se ve el stock que cree el sistema; se ve al final. En las tiendas siguen «Una familia» y «La tienda entera»; en Algete, dentro de cada ubicación se cuenta igual y F3, F4 y F6 no cambian.',
       'Ariadna', 1, 'sa', NULL
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = N'Inventario: leer el producto, «¿Cuántas hay?» y contabilizar');
SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

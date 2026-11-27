/*
    NestoAPI#914 (corte 2 de Almacén, Carlos 27/11/26: «guardar el hueco de cada lectura del recorrido: SÍ, con la próxima versión de
    Ariadna», 2.48): cada lectura del recorrido guarda el hueco de donde el mozo cogió el producto, tal como lo pinta la parada.

      PreparacionEscaneos.Hueco varchar(20) NULL   «001/002/004» (como el Motivo de las faltas). NULL = no viene (el packing no lo manda,
                                                   una Ariadna anterior, o una lectura que no sale de una parada).

    Longitud: el repo guarda un hueco como varchar(11) («002/002/004», ProductosIncompletos.HuecoOrigen, #902); aquí con holgura (20) por
    si la app manda el código de la etiqueta u otra forma. La API recorta a 20.

    Ejecutar en SSMS contra NV como sa (es DDL: el login nuevavision no tiene ALTER). Idempotente: se puede volver a lanzar.
    Orden indiferente respecto al deploy: mientras la columna no exista, la API guarda las lecturas sin ella (lo mira con COL_LENGTH y, si
    no está, vuelve a mirar cada 5 minutos: no hace falta reiniciar la API) y el indicador FaltaQueSiEstaba queda «sin rastro».

    Permisos: no hace falta GRANT (la API ya hace INSERT y SELECT en dbo.PreparacionEscaneos; añadir una columna no cambia el verbo).
    Antes de añadirla se ha revisado el repo: todos los INSERT en PreparacionEscaneos (RepositorioPreparacionAlmacen, EscriturasSalida,
    CierreReposiciones, Scripts/OneShot_20261016_Reposicion81029_QuitarFaltas.sql) llevan la lista de columnas y no hay ningún SELECT *
    ni INSERT ... SELECT * sobre la tabla: una columna NULL nueva no rompe nada.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. Sin índice propio: se lee por las claves de
    siempre (IX_PreparacionEscaneos_Origen y IX_PreparacionEscaneos_Fecha).
*/

USE NV;
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'Hueco') IS NULL
    EXEC(N'ALTER TABLE dbo.PreparacionEscaneos ADD Hueco varchar(20) NULL;');
GO

-- Comprobación: tiene que salir una fila, varchar 20, admitiendo nulos
SELECT c.name AS Columna, t.name AS Tipo, c.max_length AS Longitud, c.is_nullable AS AdmiteNulos
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND c.name = 'Hueco';
GO

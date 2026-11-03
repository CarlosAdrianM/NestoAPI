/*
    NestoAPI#783 (03/11/26): los recordatorios de respuesta de Novedades van a quien se le hizo la pregunta.

    Caso 825: la pregunta del asistente iba a @Santiago, pero el recordatorio de las 9:00 se lo mandaba al autor
    de la sugerencia (Carlos), que no tenía nada que contestar. Ahora, al marcar «EsperaRespuesta» en
    POST api/Novedades/{id}/Comentarios/Asistente, se guarda a quién se espera: los @mencionados en ese comentario
    (resueltos como en las menciones) o, sin ninguno, NULL = el autor, como hasta ahora. Los recordatorios y el
    cierre van a esas personas, y solo un comentario suyo quita la espera.

    Ejecutar en SSMS contra NV (NestoConnection), como sa, ANTES de publicar la API (la API lee la columna nueva
    en cada comentario y en el job; si se publicara antes, los comentarios se graban igual, pero la espera no se
    quita y el job falla, con aviso en ELMAH). Idempotente: se puede volver a lanzar.

    QUÉ HACE
      1) Columna nueva en dbo.Novedades:
           EsperandoRespuestaDe  nvarchar(1000) NULL   «Aplicacion|Clave|Nombre» separados por «;»
                                                        (p. ej. «Nesto|NUEVAVISION\Santiago|Santiago»).
                                                        NULL = se espera al autor (SugeridaPor).
         Las esperas que ya hay quedan en NULL: siguen esperando al autor, como antes.
      2) GRANT a la cuenta de la API (ya los tenía sobre la tabla; se repite por si acaso).

    No toca el EDMX: estas columnas se leen y escriben con SQL (RepositorioEsperaRespuestaNovedadesSql).
*/

USE NV;
GO

IF COL_LENGTH('dbo.Novedades', 'EsperandoRespuestaDe') IS NULL
    ALTER TABLE dbo.Novedades ADD EsperandoRespuestaDe nvarchar(1000) NULL;
GO

-- La API corre con la cuenta de máquina del servidor.
GRANT SELECT, UPDATE ON dbo.Novedades TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Novedades' AND COLUMN_NAME = 'EsperandoRespuestaDe';
GO

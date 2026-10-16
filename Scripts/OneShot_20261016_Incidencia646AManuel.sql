-- 16/10/26: la incidencia 646 («Recoger producto» en 928500 y 928520) la creó Claude con el usuario de Carlos,
-- en nombre de Manuel. La pasa a su autor real. El contexto automático (errores de ELMAH de Carlos) no es suyo: se quita.
-- Lanzar como sa en la BD NV.
UPDATE Novedades
   SET SugeridaPor = 'NUEVAVISION\Manuel',
       SugeridaNombre = 'Manuel',
       Contexto = NULL
 WHERE Id = 646
   AND SugeridaPor = 'NUEVAVISION\Carlos';

SELECT Id, SugeridaPor, SugeridaNombre, Categoria, Estado, Titulo FROM Novedades WHERE Id = 646;

-- NestoAPI#350: el motor de balances BPY/PGP lee LinBalance, pero la cuenta del pool de
-- IIS solo tenía SELECT sobre Balances (LinBalance solo lo tenía Administración) → el PDF
-- del balance devolvía 500 en producción (ELMAH 18/08/26 12:13, primer uso tras el deploy
-- de la 1.10.19.3; en las pruebas desde VS funcionaba porque corría con el usuario de Carlos).
-- BD NV (NestoConnection) → GRANT a la cuenta de máquina.

GRANT SELECT ON dbo.LinBalance TO [NUEVAVISION\RDS2016$];

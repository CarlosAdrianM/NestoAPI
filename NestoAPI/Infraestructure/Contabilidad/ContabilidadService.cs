using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace NestoAPI.Infraestructure.Contabilidad
{
    public class ContabilidadService : IContabilidadService
    {
        public async Task<int> ContabilizarDiario(string empresa, string diario)
        {
            using (var db = new NVEntities())
            {
                return await ContabilizarDiario(db, empresa, diario);
            }
        }

        public async Task<int> ContabilizarDiario(NVEntities db, string empresa, string diario)
        {
            var empresaParametro = new SqlParameter("@Empresa", SqlDbType.Char, 3)
            {
                Value = empresa
            };

            var diarioParametro = new SqlParameter("@Diario", SqlDbType.Char, 10)
            {
                Value = diario
            };
            //var resultadoProcedimiento = await db.Database.ExecuteSqlCommandAsync("EXEC prdContabilizar @Empresa, @Diario", empresaParametro, diarioParametro);
            //return resultadoProcedimiento;
            var resultadoParametro = new SqlParameter
            {
                ParameterName = "@Resultado",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.Output // Configurar para capturar el valor de retorno
            };
            try
            {
                // Ejecutar el procedimiento almacenado y capturar el valor de retorno
                var resultadoDirecto = await db.Database.ExecuteSqlCommandAsync("EXEC @Resultado = prdContabilizar @Empresa, @Diario", resultadoParametro, empresaParametro, diarioParametro);
                // Obtener el valor de retorno del parámetro
                var resultadoProcedimiento = (int)resultadoParametro.Value;
                return resultadoProcedimiento;
            } catch (Exception ex)
            {
                throw new Exception("Error al contabilizar el diario", ex);
            }
        }

        public async Task<int> CrearLineas(NVEntities db, List<PreContabilidad> lineas)
        {            
            foreach (var linea in lineas)
            {
                db.PreContabilidades.Add(linea);
            }
            try
            {
                await db.SaveChangesAsync();                
                return lineas.Last().Nº_Orden;
            }
            catch(Exception ex) {
                throw new Exception("No se ha podido contabilizar el diario",ex);
            }
            
        }

        public async Task<int> CrearLineas(List<PreContabilidad> lineas)
        {
            using (var db = new NVEntities())
            {
                return await CrearLineas(db, lineas);
            }
        }

        public async Task<int> CrearLineasYContabilizarDiario(List<PreContabilidad> lineas)
        {
            using (var db = new NVEntities()) {
                using (var transaction = db.Database.BeginTransaction())
                {
                    try
                    {
                        await CrearLineas(db, lineas);
                        var resultado = await ContabilizarDiario(db, lineas[0].Empresa, lineas[0].Diario);
                        if (resultado > 0)
                        {
                            transaction.Commit();                            
                        }
                        else
                        {
                            transaction.Rollback();
                        }
                        return resultado;
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        return 0;
                    }
                }
                    
            }
        }
    }
}
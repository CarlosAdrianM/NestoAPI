using NestoAPI.Models;
using NestoAPI.Models.Novedades;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.Novedades
{
    /// <summary>
    /// Issue Nesto#372: la tabla Novedades no está en el EDMX a propósito; es una tabla satélite
    /// de solo lectura para la API y se consulta con SqlQuery para no tocar el designer.
    /// Las entradas se insertan por SQL al publicar cada versión (ver Scripts/Issue372_Novedades.sql).
    /// </summary>
    public class ServicioNovedades : IServicioNovedades
    {
        public List<NovedadDTO> LeerNovedadesPublicadas()
        {
            using (NVEntities db = new NVEntities())
            {
                return db.Database.SqlQuery<NovedadDTO>(
                    "SELECT Id, Version, Fecha, Categoria, Titulo, Descripcion, Ambito " +
                    "FROM Novedades WHERE Publicada = 1").ToList();
            }
        }
    }
}

using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Infraestructure.Agencias
{
    public class ServicioEnviosAgencia
    {
        public AgenciaTransporte LeerAgencia(int id)
        {
            using (NVEntities db = new NVEntities())
            {
                return db.AgenciasTransportes.Single(a => a.Numero == id);
            }            
        }
    }
}
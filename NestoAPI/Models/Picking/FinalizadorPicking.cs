using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.Picking
{
    public class FinalizadorPicking : IFinalizadorPicking
    {
        public void Ejecutar(NVEntities db)
        {
            // throw new NotImplementedException("No se puede finalizar aún");
            try
            {
                db.SaveChanges();
            } catch (Exception ex)
            {
                throw new Exception(ex.InnerException != null ? ex.Message + "\n" + ex.InnerException.Message : ex.Message);
            }
            
        }
    }
}
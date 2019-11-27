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
            try
            {
                db.SaveChanges();
            } catch (Exception ex)
            {
                string detalle = string.Empty;

                while (ex != null)
                {
                    if (ex.Message != "An error occurred while updating the entries. See the inner exception for details.")
                    {
                        detalle += ex.Message + "\n";
                    }
                    // loop
                    ex = ex.InnerException;
                }
                throw new Exception(detalle);
            }
            
        }
    }
}
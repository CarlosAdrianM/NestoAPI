using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;

namespace NestoAPI.Infraestructure.Facturas.Agrupacion
{
    /// <summary>
    /// NestoAPI#727/#729: cuando un grupo falla a medias, lo que quedó pendiente en el contexto compartido no puede colarse
    /// en el SaveChanges del siguiente grupo ni en la facturación de rutas que viene después: lo añadido se suelta y lo
    /// modificado vuelve a sus valores originales. Nunca lanza.
    /// </summary>
    internal static class CambiosPendientesContexto
    {
        public static void Descartar(NVEntities db, string proceso)
        {
            if (db == null)
            {
                return;
            }
            try
            {
                foreach (DbEntityEntry entrada in db.ChangeTracker.Entries()
                    .Where(e => e.State != EntityState.Unchanged && e.State != EntityState.Detached).ToList())
                {
                    if (entrada.State == EntityState.Added)
                    {
                        entrada.State = EntityState.Detached;
                    }
                    else
                    {
                        entrada.CurrentValues.SetValues(entrada.OriginalValues);
                        entrada.State = EntityState.Unchanged;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[{proceso}] No se han podido descartar los cambios pendientes: {ex.Message}", ex));
            }
        }
    }
}

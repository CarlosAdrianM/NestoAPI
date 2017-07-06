using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace NestoAPI.Models.RecursosHumanos
{
    public static class GestorTiposAccion
    {
        public static List<TipoAccion> TiposAccion()
        {
            List<TipoAccion> lista = new List<TipoAccion>();
            lista.Add(new TipoAccion(1, "Jornada", true)); // Desde que ficha entrada hasta que se ficha salida
            lista.Add(new TipoAccion(2, "Comida", false));
            lista.Add(new TipoAccion(3, "Cafe Tabaco", false));
            lista.Add(new TipoAccion(4, "Asuntos Personales", false));
            lista.Add(new TipoAccion(5, "Gestiones Laborales", true));
            lista.Add(new TipoAccion(6, "Médico", true));

            return lista;
        }
    }
}
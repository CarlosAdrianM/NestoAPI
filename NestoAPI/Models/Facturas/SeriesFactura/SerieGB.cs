﻿using System.Collections.Generic;
using System.Net.Mail;

namespace NestoAPI.Models.Facturas.SeriesFactura
{
    public class SerieGB : ISerieFactura
    {
        public string RutaInforme => @"Models\Facturas\FacturaGB.rdlc";

        public List<NotaFactura> Notas => new List<NotaFactura>();

        public MailAddress CorreoDesde => throw new System.NotImplementedException();

        public string FirmaCorreo => throw new System.NotImplementedException();
    }
}
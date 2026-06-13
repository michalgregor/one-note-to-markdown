<?xml version='1.0' encoding="UTF-8"?>
<!--
  Entry stylesheet for OneNoteMarkdownExporter's MathML -> LaTeX conversion.

  It includes the unmodified XSLT MathML Library (xsltml, MIT licensed - see README in
  this folder) modules, which do the actual element-by-element MathML -> LaTeX work, and
  defines a single m:math template that emits only the LaTeX *body*. The caller wraps the
  result in $...$ or $$...$$ itself (so it can choose inline vs. block based on OneNote's
  own display attribute and keep the output Obsidian-compatible).
-->
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:m="http://www.w3.org/1998/Math/MathML"
                version='1.0'>

  <xsl:output method="text" indent="no" encoding="UTF-8"/>

  <xsl:include href="tokens.xsl"/>
  <xsl:include href="glayout.xsl"/>
  <xsl:include href="scripts.xsl"/>
  <xsl:include href="tables.xsl"/>
  <xsl:include href="entities.xsl"/>
  <xsl:include href="cmarkup.xsl"/>

  <xsl:strip-space elements="m:*"/>

  <xsl:template match="m:math">
    <xsl:apply-templates/>
  </xsl:template>

</xsl:stylesheet>

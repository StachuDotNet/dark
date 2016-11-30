import dark



class form_for(dark.Node):

  def __init__(self, action):
    self.action = action

  def get_schema(self, schema):
    output = ""
    for tag in schema.values():
      output += tag.as_tag().to_html()
    return "<form action='%s'><fieldset>%s</fieldset></form>" % (self.action,
                                                                 output)


class to_page(dark.Node):
  # TODO: this feels wrong. The schema is the markup? hmmm
  # What if we need to combine schema and data to generate the page?
  def get_schema(self, input):
    return "<html><head></head><body>" + input + "</body></html>"



class endpoint(dark.Node):
  def exe(self, input):
    raise

# class Create:
#   def __init__(self, ds, route):
#     def view(request, **values):
#       return self.server.render_template('create.html',
#                                          fields=ds.client_fields())

#     def action(request, **values):
#       new_value = request.values
#       ds.validate(new_value)
#       datastore.add(new_value)
#       return Response()

#     view_rule = Rule(route, methods=["GET"], endpoint=view)
#     action_rule = Rule(route, methods=["POST"], endpoint=action)
#     self.rules = [view_rule, action_rule]


# class List:
#   def __init__(self, ds, route):
#     def view(request, **values):
#       values = ds.fetch(10)
#       return self.server.render_template('list.html',
#                                          values=values,
#                                          fields=ds.client_fields())
#     view_rule = Rule(route, methods=["GET"], endpoint=view)
#     self.rules = [view_rule]


# class Edit:
#   def __init__(self, ds, route):

#     def view(request, **values):
#       url = request.values["url"]
#       value = request.values.by("url", url)
#       return self.render_template('edit.html', value=value)

#     def action(request, **values):
#       url = values.url
#       new_value = request.values
#       ds.validate(new_value)
#       datastore.replace(url, new_value)
#       return Response()

#     view_rule = Rule(route, methods=["GET"], endpoint=view)
#     action_rule = Rule(route, methods=["PUT"], endpoint=action)
#     self.rules = [view_rule, action_rule]


# class Read:
#   def __init__(self, ds, route):
#     def view(request, url):
#       value = ds.fetch_one(url)
#       return self.render_template('read.html', value=value)

#     rule = Rule(route, methods=["GET"], endpoint=view)
#     self.rules = [rule]
